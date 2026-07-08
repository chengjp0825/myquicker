# Aurora 名称统一重构计划

> 深度完整的名称修改定位记录 + 统一修改方案。
> 编写于 plan mode，待批准后执行。

## 1. 背景

当前名称四分五裂：产品名/exe=`aurora`，GitHub 仓库=`myquicker`，源码命名空间=`MyQuicker.*`，项目文件名=`MyQuicker.*`，文档项目名=`MyQuicker`，owner=`chengjp0825`。本次将对外品牌与源码身份统一收敛到 `aurora`。

## 2. 目标命名规范（用户已确认）

| 维度 | 现状 | 目标 |
|---|---|---|
| GitHub 仓库 | `chengjp0825/myquicker` | `chengjp0825/aurora`（重命名） |
| 源码命名空间 | `MyQuicker.*` | `Aurora.*`（PascalCase，符合 C# 规范） |
| 项目文件名 | `MyQuicker.csproj` / `.sln` / `Tests/` / `.wxs` | `aurora.*`（lowercase，与产品名一致） |
| 程序集名(AssemblyName) | 主=`aurora`，测试=`MyQuicker.Tests`（默认） | 主=`aurora`（不变），测试=`aurora.Tests`（默认跟文件名） |
| 文档项目名叙述 | `MyQuicker` | `aurora` |
| `docs/msi-publish-setup.html` | 旧版（Name=MyQuicker 等），与 .md 严重不一致 | 基于新 .md 重新生成 |
| owner | `chengjp0825` | 不变 |

**关键区分**：代码标识符（命名空间、类名）用 `Aurora`（PascalCase）；产品名/仓库名/程序集名/文件名用 `aurora`（lowercase）。两者独立。

**已就绪（本次不动）**：csproj 元数据(AssemblyName/Company/Product=aurora)、app.manifest(aurora.app)、App.xaml.cs(Toast/NotifyIcon="aurora")、wxs 内容(全 aurora)、build-msi.ps1(aurora-$Version.msi)、release.yml(aurora-$v.msi, aurora-msi)、LICENSE(Copyright aurora)、UpdateChecker.cs(ReleasesUrl=chengjp0825/aurora, UA=aurora-Updater)、test-install-lifecycle.ps1。

## 3. 全量定位记录（改点清单）

### A. 源码命名空间 `MyQuicker.*` → `Aurora.*`

- **`.cs` namespace 声明**：120 个文件，每个 1 处。覆盖 `src/Domain/{Runtime,DTO}/`、`src/Services/`、`src/UI/`、`src/Interop/`、`Models/`、`Converters/`、根 `App.xaml.cs`/`AssemblyInfo.cs` 等，以及 `tests/MyQuicker.Tests/` 下全部测试文件。
- **`.cs` using 语句**：135 处 / 69 文件。`using MyQuicker.X` → `using Aurora.X`。
- **XAML `x:Class`**（6 文件）：
  - `App.xaml:1` `x:Class="MyQuicker.App"`
  - `UI/ToastWindow.xaml:1` `x:Class="MyQuicker.UI.ToastWindow"`
  - `UI/MainWindow.xaml:1` `x:Class="MyQuicker.UI.MainWindow"`
  - `UI/SettingsWindow.xaml:1` `x:Class="MyQuicker.UI.SettingsWindow"`
  - `UI/PinWindow.xaml:1` `x:Class="MyQuicker.UI.PinWindow"`
  - `UI/ScreenshotWindow.xaml:1` `x:Class="MyQuicker.UI.ScreenshotWindow"`
- **XAML `clr-namespace`**：
  - `App.xaml:4` `xmlns:local="clr-namespace:MyQuicker"`
  - `App.xaml:5` `xmlns:conv="clr-namespace:MyQuicker.Converters"`
  - `UI/MainWindow.xaml:6` `xmlns:dto="clr-namespace:MyQuicker.Domain.DTO"`

### B. 项目文件名（`git mv` 保留历史）

| 旧路径 | 新路径 |
|---|---|
| `MyQuicker.csproj` | `aurora.csproj` |
| `MyQuicker.sln` | `aurora.sln` |
| `tests/MyQuicker.Tests/`（目录） | `tests/aurora.Tests/` |
| `tests/MyQuicker.Tests/MyQuicker.Tests.csproj` | `tests/aurora.Tests/aurora.Tests.csproj` |
| `installer/MyQuicker.wxs` | `installer/aurora.wxs` |

### C. 项目引用与 InternalsVisibleTo

- **`aurora.sln`**（行 6、8）：Project name `"MyQuicker"`→`"aurora"`、`"MyQuicker.Tests"`→`"aurora.Tests"`；path `"MyQuicker.csproj"`→`"aurora.csproj"`、`"tests\MyQuicker.Tests\MyQuicker.Tests.csproj"`→`"tests\aurora.Tests\aurora.Tests.csproj"`。**GUID 不变**（B31E12D9…、2F598209…）。
- **`aurora.csproj:34`**：`<InternalsVisibleTo Include="MyQuicker.Tests" />` → `"aurora.Tests"`（程序集名）
- **`AssemblyInfo.cs:4`**：`[assembly: InternalsVisibleTo("MyQuicker.Tests")]` → `"aurora.Tests"`（与上同，程序集名 lowercase）
- **`tests/aurora.Tests/aurora.Tests.csproj:25`**：`<ProjectReference Include="..\..\MyQuicker.csproj" />` → `"..\..\aurora.csproj"`

### D. 构建/CI 引用

- `installer/build-msi.ps1:13`：`"$root/MyQuicker.csproj"` → `"$root/aurora.csproj"`
- `installer/build-msi.ps1:66`：`"$root/installer/MyQuicker.wxs"` → `"$root/installer/aurora.wxs"`
- `.github/workflows/release.yml:27`：`dotnet publish MyQuicker.csproj` → `aurora.csproj`
- `.github/workflows/release.yml:77`：`wix build installer/MyQuicker.wxs installer/harvest.wxs` → `installer/aurora.wxs installer/harvest.wxs`

### E. 文档叙述 `MyQuicker` → `aurora`

- `CLAUDE.md:3`：项目介绍句
- `CONTEXT.md:1,5,139`：标题 "MyQuicker Domain Glossary" + 正文 "MyQuicker menu"
- `docs/adr/0001-trigger-as-umbrella-concept.md:9`："MyQuicker wakes its menu…"
- `docs/adr/0002-separate-settings-dto-from-runtime-objects.md:9`："MyQuicker persists user configuration…"
- `docs/known-issues.md`：行 19、38 的涉及文件路径（`MyQuicker.csproj`、`tests/MyQuicker.Tests/`）+ 其他叙述
- `docs/msi-publish-setup.md`：多处 `MyQuicker.csproj`/`MyQuicker.wxs` 文件引用 + §4 line 247 注释"源码命名空间保留 MyQuicker.*"（需更新为 Aurora.*）+ §附 line 509 "MyQuicker/aurora"（已过时，改 chengjp0825/aurora）

### F. `docs/msi-publish-setup.html` 重新生成

整文件是旧版（Name="MyQuicker"、MyQuicker.cab、MyQuicker.exe、MyQuicker-$Version.msi、your-name、PublishSingleFile 等），与 .md 全面冲突。基于当前 .md 重新生成（pandoc 转 HTML 或手写）。

### G. git remote + GitHub 仓库重命名

- 本地：`git remote set-url origin git@github.com:chengjp0825/aurora.git`
- GitHub：用户在仓库 Settings → General → Repository name 改 `myquicker` → `aurora`（gh CLI 未安装，需网页操作）

## 4. 执行步骤（有序）

1. **`git mv` 文件/目录改名**（B 节）— 5 个移动，保留 git 历史。
2. **全局替换源码命名空间** `MyQuicker` → `Aurora`（A 节）— `.cs`（namespace + using）+ `.xaml`（x:Class + clr-namespace）。代码文件里 `MyQuicker` 全是命名空间标识符，机械替换安全。
3. **改项目引用与 InternalsVisibleTo**（C 节）— `.sln`、`aurora.csproj`、`AssemblyInfo.cs`、测试 csproj 的 ProjectReference。注意 InternalsVisibleTo 用程序集名 `aurora.Tests`（lowercase）。
4. **改构建/CI 引用**（D 节）— `build-msi.ps1`、`release.yml` 里的 csproj/wxs 路径。
5. **改文档叙述**（E 节）— CLAUDE.md、CONTEXT.md、ADR、known-issues.md、msi-publish-setup.md。文档里 `MyQuicker` 是项目名叙述，改 `aurora`；文件引用改 `aurora.csproj`/`aurora.wxs`。
6. **重新生成 .html**（F 节）— 基于新 .md。
7. **`git remote set-url`**（G 节）— 本地 remote 指向 aurora.git。
8. **验证**：
   - `dotnet build -c Release` — 0 警告 0 错误
   - `dotnet test` — 154 通过
   - `wix build installer/aurora.wxs installer/harvest.wxs …` — EXIT=0
   - `./installer/test-install-lifecycle.ps1 -Build` — 17/17 PASS
9. **用户 GitHub 重命名仓库**（G 节）— 网页操作，重命名后本地 push 即可生效。

## 5. 风险与注意

- **InternalsVisibleTo 用程序集名** `aurora.Tests`（lowercase，跟测试 csproj 文件名），**不是**命名空间 `Aurora.Tests`。`aurora.csproj` 与 `AssemblyInfo.cs` 两处都要改且一致。
- **XAML `x:Class` 必须与 `.cs` 的 `namespace` 同步**，否则编译失败（WPF 生成代码绑定 x:Class）。
- **`.sln` GUID 保留**，只改 Project name 与 path。
- **`git mv` 保留历史**，优于删+建。
- **替换边界**：`.cs`/`.xaml` 里 `MyQuicker` 全是命名空间标识符 → 替换为 `Aurora`；文档里 `MyQuicker` 是项目名叙述 → 替换为 `aurora`。两类分开处理，大小写不同。
- **`harvest.wxs`** 被 `.gitignore`，自动生成，不需手改。
- **glm-latest API 限制**（CLAUDE.md 备注）：`git mv`/`git remote set-url` 是写操作，分类器可能阻塞，需用户手动批准那次工具调用。
- **GitHub 仓库重命名**：旧 URL 自动 301 重定向，但 `api.github.com/repos/chengjp0825/aurora` 需重命名后才存在。重命名前 push 会失败（仓库不存在），故步骤 9 在步骤 7 之后、push 之前。

## 6. 不在本次范围

- 修复 KI-3~KI-23 已知缺陷（独立任务）
- 数字签名、README、Release notes（发布物料，独立任务）
- 生命周期测试接入 CI（可选增强）
