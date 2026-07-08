using System.IO;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates golden JSON files for RpcIntegrationTests.
/// Menu: UniLiquidLink > Generate RPC Golden Files
/// Run this once after any intentional behavior change to update the golden files.
/// </summary>
public static class GenerateRpcGolden
{
    [MenuItem("UniLiquidLink/Tests/Generate RPC Golden Files")]
    public static void Generate()
    {
        string goldenDir = RpcIntegrationTests.GoldenDir();
        if (!Directory.Exists(goldenDir))
        {
            Directory.CreateDirectory(goldenDir);
        }

        SetupIntegrationTest.Setup();
        var ctx = new RpcTestContext();

        // 1. SampleMethodInt
        string r1 = ctx.SendAndGetResponse(@"{""jsonrpc"":""2.0"",""id"":1,""method"":""RpcTestContext.SampleMethodInt"",""params"":[42]}");
        WriteGolden(goldenDir, "golden_sample_method_int", r1);

        // 2. SampleVector3
        string r2 = ctx.SendAndGetResponse(@"{""jsonrpc"":""2.0"",""id"":2,""method"":""RpcTestContext.SampleVector3"",""params"":[{""x"":1,""y"":2,""z"":3}]}");
        WriteGolden(goldenDir, "golden_sample_vector3", r2);

        // 3. Unknown method → error response.
        // The harness re-throws RPC errors via OnError, but the error response is already
        // recorded before the throw, so recover it from the captured responses.
        try
        {
            ctx.SendAndGetResponse(@"{""jsonrpc"":""2.0"",""id"":3,""method"":""NoSuchMethod"",""params"":[]}");
        }
        catch
        {
            // Expected: the unknown-method dispatch error is surfaced by the harness.
        }
        string r3 = ctx.Responses[^1];
        WriteGolden(goldenDir, "golden_unknown_method_error", r3);

        // 4. Find
        string r4 = ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":4,""method"":""UnityEngine.GameObject.Find"",""params"":[""UniLiquidLinkTestObject""]}");
        WriteGolden(goldenDir, "golden_find", RpcTestContext.NormalizeInstanceIds(r4));

        // Parse the live goRef (instanceId preserved) for subsequent chain calls
        string goRpcObj = JsonDocument.Parse(r4).RootElement.GetProperty("result").GetRawText();

        // 5. ResolveChain: go → .transform
        string req5 = string.Format(
            @"{{""jsonrpc"":""2.0"",""id"":5,""method"":""JsonRpc_ResolveChain"",""params"":[{0},[],""transform"",null]}}",
            goRpcObj);
        string r5 = ctx.SendAndGetResponse(req5);
        WriteGolden(goldenDir, "golden_resolve_chain_get_transform", RpcTestContext.NormalizeInstanceIds(r5));

        // Parse the live tRef for the Rotate call
        string tRpcObj = JsonDocument.Parse(r5).RootElement.GetProperty("result").GetRawText();

        // 6. ResolveChain: go → [transform step] → .gameObject
        string req6 = string.Format(
            @"{{""jsonrpc"":""2.0"",""id"":6,""method"":""JsonRpc_ResolveChain"",""params"":[{0},[{{""name"":""transform""}}],""gameObject"",null]}}",
            goRpcObj);
        string r6 = ctx.SendAndGetResponse(req6);
        WriteGolden(goldenDir, "golden_resolve_chain_chained_step", RpcTestContext.NormalizeInstanceIds(r6));

        // 7. ResolveChain: transform → Rotate(10, 20, 30, Space.Self)
        string req7 = string.Format(
            @"{{""jsonrpc"":""2.0"",""id"":7,""method"":""JsonRpc_ResolveChain"",""params"":[{0},[],""Rotate"",[10.0,20.0,30.0,{{""value"":""Self"",""rpcEnum"":1}}]]}}",
            tRpcObj);
        string r7 = ctx.SendAndGetResponse(req7);
        WriteGolden(goldenDir, "golden_resolve_chain_rotate", r7);

        // 8. ResolveChainSet: go → [transform step] → set position, then read it back
        string req8Set = string.Format(
            @"{{""jsonrpc"":""2.0"",""id"":8,""method"":""JsonRpc_ResolveChainSet"",""params"":[{0},[{{""name"":""transform""}}],""position"",{{""x"":1,""y"":2,""z"":3}}]}}",
            goRpcObj);
        ctx.SendAndGetResponse(req8Set);

        string req9Get = string.Format(
            @"{{""jsonrpc"":""2.0"",""id"":9,""method"":""JsonRpc_ResolveChain"",""params"":[{0},[{{""name"":""transform""}}],""position"",null]}}",
            goRpcObj);
        string r9 = ctx.SendAndGetResponse(req9Get);
        WriteGolden(goldenDir, "golden_resolve_chain_set_position", r9);

        AssetDatabase.Refresh();
        Debug.Log("[GenerateRpcGolden] Done. Files written to: " + goldenDir);
    }

    static void WriteGolden(string dir, string name, string json)
    {
        string path = Path.Combine(dir, name + ".json");
        File.WriteAllText(path, json);
        Debug.Log("[GenerateRpcGolden] Written: " + name + ".json");
    }
}
