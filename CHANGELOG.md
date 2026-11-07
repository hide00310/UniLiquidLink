# CHANGELOG

<!-- version list -->

## v2.0.0 (2026-11-07)

### Bug Fixes

- **csharp**: Let arrays of converter-backed elements reach their converter
  ([`a82e9ce`](https://github.com/hide00310/UniLiquidLink/commit/a82e9cef8deed84c18ef663f305422706b4158dc))

- **csharp**: Write Unity fake-null objects as JSON null
  ([`b7e4f26`](https://github.com/hide00310/UniLiquidLink/commit/b7e4f26589c57eef3e7dd1db26357ebcda749248))

- **python**: Attach the failing method and params to RPC errors
  ([`937cb75`](https://github.com/hide00310/UniLiquidLink/commit/937cb757a7cce8b98628c91d0fa8b53a6027227e))

- **python**: Drop the decoded frame so ObjectProxy can be released
  ([`ed28817`](https://github.com/hide00310/UniLiquidLink/commit/ed28817cbb8c4953e6f73fbaec40d59aa66f0665))

- **python**: Keep RpcError type and attach the failing call
  ([`b44f244`](https://github.com/hide00310/UniLiquidLink/commit/b44f24481a74b32374ab79dc7fb7fe0a84084d07))

- **python**: Reuse one ObjectProxy per Unity instanceId
  ([`457d433`](https://github.com/hide00310/UniLiquidLink/commit/457d4332f190448a3d90ee43e00d32603bd9b608))

### Chores

- **docs**: Regenerate class diagram and API docs
  ([`790978d`](https://github.com/hide00310/UniLiquidLink/commit/790978d2a3f94e74743991f275ac2187a1444336))

- **docs**: Regenerate class diagram and API docs for Client.run
  ([`148268d`](https://github.com/hide00310/UniLiquidLink/commit/148268d2c1dc8c425678c79ca4a5d4cbdd5e9a1b))

- **tools**: Delete tools~/Analyzer csproj files and tools~/tmp.py
  ([`175c83a`](https://github.com/hide00310/UniLiquidLink/commit/175c83a15ff0cc05757f75ce2d22793387097ad0))

- **tools**: Restore tools~/Analyzer csproj files
  ([`2022026`](https://github.com/hide00310/UniLiquidLink/commit/2022026fb2b8ca9809cf3f20d320b3df25e80c37))

- **unity**: Drop Models/Schema.cs.meta
  ([`3e3a4c6`](https://github.com/hide00310/UniLiquidLink/commit/3e3a4c6294741ae22877c85becab71e1a6f3d8c4))

- **unity**: Restore Models/Schema.cs.meta
  ([`80d4ee5`](https://github.com/hide00310/UniLiquidLink/commit/80d4ee5c8075e8fce98970149d09926dbd0e8edd))

- **unity**: Rewrite guid in 93 .meta files and drop 10 Samples~ .meta files
  ([`e8b6a06`](https://github.com/hide00310/UniLiquidLink/commit/e8b6a0644c658a6630aa7dc91a1ad5ce1da22015))

### Documentation

- **readme**: Refresh Features examples and add Python Scripting item
  ([`ba95c3e`](https://github.com/hide00310/UniLiquidLink/commit/ba95c3e323d1a4ce56efd14033e8fac5471d4968))

### Features

- **rpc**: Add RPC timing stats across client, gateway and Unity
  ([`f245f09`](https://github.com/hide00310/UniLiquidLink/commit/f245f090184026354929ea085a3d52ca509eca79))

### Performance Improvements

- **csharp**: Keep the main-thread drain alive across a burst of RPCs
  ([`d62a083`](https://github.com/hide00310/UniLiquidLink/commit/d62a083b418a0590a49642ca839df15c8280b685))

### Refactoring

- **python**: Rename Client.on_execute event to run
  ([`a42c8b2`](https://github.com/hide00310/UniLiquidLink/commit/a42c8b2ec8ffdbf6989b1a060cb288d104d8db04))

### Breaking Changes

- **python**: Client.on_execute is removed; register handlers with `client.run += handler` instead.


## v1.3.0 (2026-08-02)

### Bug Fixes

- **csharp**: Format array params with ArrayLogFormatter in ResolveChain log
  ([`ef2a319`](https://github.com/hide00310/UniLiquidLink/commit/ef2a31934fa9210aa56364444af4c9cceed6b2d6))

- **csharp**: Guard ArrayLogFormatter.ToString against a null collection
  ([`12e1d4c`](https://github.com/hide00310/UniLiquidLink/commit/12e1d4c5a7cd8b5e1a7856b2bc0dcfbef398b583))

- **csharp**: Resolve Data~ directory via Utils.ResolveDataDir
  ([`bc35e34`](https://github.com/hide00310/UniLiquidLink/commit/bc35e34d452bf7e0552be5ea2e199947cf97cfea))

- **csharp**: Unwrap TargetInvocationException in RPC dispatch
  ([`8c9faf2`](https://github.com/hide00310/UniLiquidLink/commit/8c9faf2b86a47916c6fc8289f953faf166267dbb))

- **python**: Respect pre-configured log level in setup_logger
  ([`638b8a9`](https://github.com/hide00310/UniLiquidLink/commit/638b8a907b6d1f957afee2e94ce2172445e7856a))

- **tests**: Resolve Samples types via reflection in SampleServerTest
  ([`f5431d9`](https://github.com/hide00310/UniLiquidLink/commit/f5431d9176b36cc34f2f3e0a7562b01ef24346bd))

- **tools**: Correct clean_meta target paths and re-enable make()
  ([`78b6ec1`](https://github.com/hide00310/UniLiquidLink/commit/78b6ec10eea9e60d166961df71e5b43d0aec7af5))

- **uniliquidlink**: Raise resolver.py missing-CSV logs from warning to error
  ([`1d8b3da`](https://github.com/hide00310/UniLiquidLink/commit/1d8b3da75904d471f666c0b54ab87ec9845b4796))

### Chores

- Add C# .editorconfig for repo-wide style/analyzer rules
  ([`49210e7`](https://github.com/hide00310/UniLiquidLink/commit/49210e7dbdc4e958e25fcf656f6744b0eaa32a4e))

- Re-track meta file for Models/Schema.cs
  ([`8fafece`](https://github.com/hide00310/UniLiquidLink/commit/8fafece265f80dfc7770bf12ca5cd6f76a7560f6))

- Remove stale .meta files for non-Unity package sources
  ([`d7cfd06`](https://github.com/hide00310/UniLiquidLink/commit/d7cfd06f11670d6967240b3e4d9e1c52f762927b))

- Remove stale meta file for deleted Schema.cs
  ([`b3769a8`](https://github.com/hide00310/UniLiquidLink/commit/b3769a8a7e96cb49711b0589d1a3128110b4ee2b))

- Remove stale meta file for deleted Schema.cs
  ([`d2bbf3d`](https://github.com/hide00310/UniLiquidLink/commit/d2bbf3d5034de808d258533f05d37179952dc45e))

- Replace Unity .gitignore template with dotnet CLI template
  ([`1e32ff8`](https://github.com/hide00310/UniLiquidLink/commit/1e32ff8c7bd69ec2c8184dd9e4d68c14ec381b6d))

- Track .meta files for UPM package installs
  ([`f5ff85d`](https://github.com/hide00310/UniLiquidLink/commit/f5ff85df7b0be0f95a79a25b463375334bfe31b3))

- Track Data~ directory while ignoring only its CSV contents
  ([`1761c6d`](https://github.com/hide00310/UniLiquidLink/commit/1761c6d1735555a8d24855a864810437094dc597))

- Track meta file for Models/Schema.cs
  ([`6ea67d6`](https://github.com/hide00310/UniLiquidLink/commit/6ea67d621a011c51c15dd4c384191c389a0f3abf))

- **docs**: Regenerate class diagram and API docs
  ([`4313363`](https://github.com/hide00310/UniLiquidLink/commit/4313363d0769514df4a609e1a19d63a930d78585))

- **docs**: Regenerate class diagram and Python API docs
  ([`aba54ae`](https://github.com/hide00310/UniLiquidLink/commit/aba54ae9f65f15fe343f13bb19810529e58891d4))

- **samples**: Enable debug logging in all_features_tour.py
  ([`b7df936`](https://github.com/hide00310/UniLiquidLink/commit/b7df936b2308630a1c59a59992252e36ff56201e))

- **samples**: Enable debug logging in run_middleware_server.py
  ([`6d07335`](https://github.com/hide00310/UniLiquidLink/commit/6d07335919f48ca10e1f29ba60aca807f1be3e8e))

- **samples**: Set lliquidlink logger before importing client/server
  ([`ac50915`](https://github.com/hide00310/UniLiquidLink/commit/ac509156e8f2ef45d7fc34ec21cd8641353b6ef8))

- **tests**: Remove stale .meta files under Tests/Python~
  ([`5d104e3`](https://github.com/hide00310/UniLiquidLink/commit/5d104e380b878d7c18976e7f2fc1ac47ab7cf8dc))

### Continuous Integration

- **release**: Ignore new sample-server pytest files in CI
  ([`2e65a81`](https://github.com/hide00310/UniLiquidLink/commit/2e65a8105212cf8933f3837a508cfccf3ebd6c97))

### Documentation

- Regenerate docs into Docs~, drop stale Docs directory
  ([`84062fd`](https://github.com/hide00310/UniLiquidLink/commit/84062fdb338043b98892658c860c9e76a4afea31))

### Features

- Enable logger control and required dataDir arg for sample servers
  ([`69a4dca`](https://github.com/hide00310/UniLiquidLink/commit/69a4dcafc48de36f213aaa9133c25bad9fca4164))

- List Docs~, Python~, and tools~ as package samples
  ([`bcde84f`](https://github.com/hide00310/UniLiquidLink/commit/bcde84f2877047fccb39514ded682610be956ede))

- **samples**: Register abbreviated classes in CubeDemo sample
  ([`5392a6c`](https://github.com/hide00310/UniLiquidLink/commit/5392a6c97efe53a77ee565d33d13d4c0fdf7b6b1))

- **tests**: Add menu-driven start/stop for sample servers
  ([`bd7b4b8`](https://github.com/hide00310/UniLiquidLink/commit/bd7b4b85d018893ec1bc4310cd2ee099fa47f53b))

- **tests**: Batch-mode Samples import and Cube Demo/Tour auto-start
  ([`9776b5e`](https://github.com/hide00310/UniLiquidLink/commit/9776b5e164bd3d1d1b69e4badc875e2b55965fc2))

### Refactoring

- Hide UniLiquidLink/CSharp/LLiquidLink/Data from Unity by renaming to Data~
  ([`dd6aa15`](https://github.com/hide00310/UniLiquidLink/commit/dd6aa15f8cfd7afdef522fb25e912655792002e7))

- **core**: Split RpcRegistrar/RpcBus into RpcRegistry, RpcSearcher, MethodCaller
  ([`e5def5f`](https://github.com/hide00310/UniLiquidLink/commit/e5def5f548efb9289be6ccac2abc6871338affde))

- **python**: Decouple proxies from Client, extract ReleaseManager, add Protocol interfaces
  ([`ca23bb6`](https://github.com/hide00310/UniLiquidLink/commit/ca23bb61629edebde77e80cc00d1254ff1c66575))

- **python**: Remove forward-reference quotes from lliquidlink type hints
  ([`544c8a2`](https://github.com/hide00310/UniLiquidLink/commit/544c8a25589c2202bd93cb3901641a2cb5318f57))

- **samples**: Extract main() in All Features Tour sample script
  ([`216129b`](https://github.com/hide00310/UniLiquidLink/commit/216129b727f8590697a0178de037f92a46da05d0))

- **samples**: Extract main() in Cube Demo sample script
  ([`d7de029`](https://github.com/hide00310/UniLiquidLink/commit/d7de0292aaa744f47d697c13b2d07f11eff1ca23))

- **tools**: Make clean_meta touch .meta files instead of deleting
  ([`3ff55ac`](https://github.com/hide00310/UniLiquidLink/commit/3ff55ac5cf2f91f9752c57b264387dacc6ecb308))

- **tools**: Rename tools/ to tools~ and point generated output at Docs~
  ([`4e42c4e`](https://github.com/hide00310/UniLiquidLink/commit/4e42c4e18a7fe0072749012bad9cd3ec7bfd1161))

- **tools**: Split clean_meta out of make.py into its own script
  ([`56b0d89`](https://github.com/hide00310/UniLiquidLink/commit/56b0d89be2d4028ec9453d681025e1bc57540e50))

- **tools**: Unify lliquidlink class diagram generation into one pass
  ([`a3d7420`](https://github.com/hide00310/UniLiquidLink/commit/a3d7420e293aaad428664f36c099343cc42667d6))

### Testing

- **csharp**: Update tests for the RpcRegistry/MethodCaller split
  ([`5c1c461`](https://github.com/hide00310/UniLiquidLink/commit/5c1c4615c6839e080b1318927e90b6f44f984bc8))

- **python**: Add pytest coverage for CubeDemo/AllFeaturesTour samples
  ([`50b3e70`](https://github.com/hide00310/UniLiquidLink/commit/50b3e704f6b24afff2e67ffb009ea16bedd984df))

- **python**: Update python tests and goldens for the client/server refactor
  ([`d6f6ca0`](https://github.com/hide00310/UniLiquidLink/commit/d6f6ca0d2d904d5386988888f82adc7b9a7dd78c))


## v1.2.0 (2026-07-11)

### Bug Fixes

- Log JSON-RPC error responses in JsonRpcPeer._dispatch
  ([`f90b53a`](https://github.com/hide00310/UniLiquidLink/commit/f90b53ad385116887c9e69e1f0b3f14b8217d6a1))

- Reduce default log level to INFO, trim All Features Tour demo
  ([`ad10361`](https://github.com/hide00310/UniLiquidLink/commit/ad10361995163f698de7e1e4d61b8b09be77ede4))

- Refactor Python initialization and improve logger setup
  ([`189c712`](https://github.com/hide00310/UniLiquidLink/commit/189c712519325f0f909f5b01c5147b071bc13b9b))

### Documentation

- Update README for pip package availability and bulk RPC registration rename
  ([`f620547`](https://github.com/hide00310/UniLiquidLink/commit/f620547b6e4a888a0d866dc5a2cc56bc736da6b3))

### Features

- Forward Python middleware stderr through StdioTransport OnError
  ([`334c538`](https://github.com/hide00310/UniLiquidLink/commit/334c538dc5700cfb06b271885a14245eefb7bd1a))

### Refactoring

- Unify Cube Demo and All Features Tour into one sample window
  ([`9766222`](https://github.com/hide00310/UniLiquidLink/commit/97662223b97fe64c867ec9259174832cfc090dcb))


## v1.1.0 (2026-07-11)


## v1.0.0 (2026-07-09)

- Initial Release
