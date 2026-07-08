using LLiquidLink;
using NUnit.Framework;
using System.Collections.Generic;
using UniLiquidLink;
using UnityEngine;

[TestFixture]
public class ObjectRegistryTests
{
    ObjectRegistry _reg;

    [SetUp]
    public void SetUp()
    {
        _reg = new ObjectRegistry(() => new UniLiquidLink.Server.NullLogger());
    }

    [Test]
    public void RegisterAndGet_ByInstanceId()
    {
        var go = new GameObject("__ObjReg_Register");
        try
        {
            long id = _reg.RegisterObject(go);
            Assert.AreEqual(go, _reg.GetObject(id));
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void Register_Null_DoesNothing()
    {
        int before = _reg._objectMap.Count;
        _reg.RegisterObject(null);
        Assert.AreEqual(before, _reg._objectMap.Count);
    }

    [Test]
    public void RemoveObject_FiresEvent_AndRemovesEntry()
    {
        var go = new GameObject("__ObjReg_Remove");
        try
        {
            int id = (int)_reg.RegisterObject(go);
            var removed = new List<int>();
            _reg.OnRemoveObject += i => removed.Add(i);
            _reg.RemoveObject(id);
            Assert.AreEqual(1, removed.Count);
            Assert.AreEqual(id, removed[0]);
            Assert.IsFalse(_reg._objectMap.ContainsKey(id));
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void UnregisterObject_RemovesFromMap()
    {
        var go = new GameObject("__ObjReg_Unregister");
        try
        {
            long id = _reg.RegisterObject(go);
            _reg.UnregisterObject(go);
            Assert.IsFalse(_reg._objectMap.ContainsKey(id));
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void ClearObjectMap_EmptiesAll()
    {
        var go1 = new GameObject("__ObjReg_Clear1");
        var go2 = new GameObject("__ObjReg_Clear2");
        try
        {
            _reg.RegisterObject(go1);
            _reg.RegisterObject(go2);
            _reg.ClearObjectMap();
            Assert.AreEqual(0, _reg._objectMap.Count);
        }
        finally
        {
            Object.DestroyImmediate(go1);
            Object.DestroyImmediate(go2);
        }
    }
}
