# 有状态 Singleton 线程安全审计（fix-plan F8）

> 日期：2026-08-15
> 范围：`App.xaml.cs` 中以 `AddSingleton` 注册、持有可变状态且被多页面/后台共享的服务
> 基线：审计前 248/248 测试通过

## 总览

| 服务 | 审计前状态 | 结论 | 处理 |
|---|---|---|---|
| `AITaskCenterService` | `lock(_sync)` 全覆盖 | 达标 | 无改动 |
| `AppSettings` | `lock(Sync)` 全覆盖 | 达标 | 无改动 |
| `McpServerManager` | `_clients` 用 ConcurrentDictionary，但 `_configs`（普通 List）无锁 | **缺陷** | 已修复 |
| `CleanerStateStore` | 按文件粒度 SemaphoreSlim + 临时文件原子替换 | 达标 | 补注释 |
| `AISharedContextService` | 可变状态锁内交换，CleanerScanReport 克隆进出 | 达标 | 补注释 |

## McpServerManager（修复）

### 缺陷

`_configs` 是普通 `List<McpServerConfig>`，修复前完全无锁，且被两类线程并发访问：

- **UI 线程**：设置页 `AddOrUpdateServer` / `RemoveServer` / `GetServers` 枚举（`SettingsPage.xaml.cs`）。
- **后台线程**：AI 会话循环触发的 `StartAllEnabledServersAsync` / `StartServerAsync` 枚举与查询。

具体风险：

1. `AddOrUpdateServer` 的 `List.Add` 与 `StartAllEnabledServersAsync` 的 `Where` 枚举并发 → `InvalidOperationException`（集合在枚举期间被修改）。
2. `GetServers()` 返回 `_configs.AsReadOnly()` 包装而非快照，调用方枚举期间集合被增删 → 同上。
3. 并发 `SaveConfigs` 对同一 `.tmp` 文件 `File.Create` + `File.Move` → `IOException`，可能写坏配置文件。

### 修复方式

- 新增 `_configLock`，所有 `_configs` 读写均在锁内；注释声明线程模型。
- `GetServers()` 返回锁内 `ToList()` 快照。
- `StartAllEnabledServersAsync` / `StartServerAsync` 在锁内取快照/查询，锁外执行 `await`（不持锁跨 await）。
- `SaveConfigs` 全程持锁（序列化 + 写盘串行化），事件在锁外且仅写盘成功时触发。
- 构造函数新增可选 `configFilePath` 参数（默认行为不变），用于单元测试目录隔离。

### 验证

新增 `ConcurrentAddEnumerateAndRemove_NoExceptionsOrLostUpdates`：8 路并发新增 + 4 路并发枚举/启动扫描 + 2 路并发删除，无异常且最终数量精确。

## CleanerStateStore（达标，补注释）

线程模型：每个持久化文件对应一把独立 `SemaphoreSlim`（按文件路径键，`ConcurrentDictionary.GetOrAdd`），同一文件的读/改/写互斥；写入一律"临时文件 + `File.Move` 原子替换"。

- `UpdatePreferencesAsync` 的读-改-写在同一把锁内完成，无丢失更新窗口。
- `LoadAuditAsync` 的版本迁移在 audit 锁外执行（需读 history 文件），并发调用可能重复迁移一次；迁移是幂等重算，结果一致，无需加锁。已写入类级注释。
- `SaveHistoryAsync` 入参集合在锁内被拷贝后序列化；契约是"调用方交出后不得继续修改"，已写入方法注释。

验证：新增 `UpdatePreferencesAsync_ParallelIncrementsHaveNoLostUpdates`（60 路并行自增，最终值精确等于 60）。

## AISharedContextService（达标，补注释）

线程模型：可变状态全部在 `_sync` 锁内交换。

- `CleanerScanReport`：进出均深克隆，订阅者与调用方持有独立副本（已有测试覆盖：外部修改不影响内部状态）。
- `AIMediaAnalysisContext` / `AIMediaOrganizationPreview`：属性全部 init-only，按约定创建后不再修改（含内部集合），直接共享引用是安全的。此契约已写入类级注释：若未来改为可变模型，必须同步改为克隆进出。

验证：新增 `ParallelSetAndGet_ConsistentSnapshotsWithoutCorruption`（4 写 + 4 读并行 200 轮，快照完整可读）。

## 后续约定

- 新增有状态 Singleton 时，必须在类头部注释声明线程模型（锁 / 不可变交换 / 单线程限定）。
- 对外返回集合的方法，若内部集合可变，一律返回快照而非包装视图。
