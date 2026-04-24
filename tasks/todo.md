# Auth & Licensing — Phase 1

## Context

Phase 1 (university submission): validate a license key + HWID via Keygen.sh. Internet required on every launch. If offline or HWID mismatch → read-only mode (UI visible, all actions disabled).

Phase 2 (factory): client receives source code + binary. They delete the auth files themselves. No build flags needed.

**Files to delete for Phase 2**: `LicenseService.cs`, `LicenseActivationWindow.axaml/.cs`, and ~15 lines in `App.axaml.cs`.

---

## Tasks

- [ ] 1. Create `logistic/LicenseService.cs`
- [ ] 2. Create `logistic/LicenseActivationWindow.axaml` + `.cs`
- [ ] 3. Modify `logistic/SettingsWindow.axaml.cs` — promote button fields, `ApplyReadOnly()`
- [ ] 4. Modify `logistic/MainWindow.axaml` — add `ReadOnlyBanner`
- [ ] 5. Modify `logistic/MainWindow.axaml.cs` — `SetReadOnly()`, pass flag to SettingsWindow
- [ ] 6. Modify `logistic/App.axaml.cs` — async startup gate

---

## Spec

### 1. LicenseService.cs (new)

Static class — same pattern as `ContainerSpec`/`ProductSpec`. No DI.

```csharp
internal static class LicenseService
{
    public static bool IsActivated { get; private set; }

    // Storage: %APPDATA%/logistic/license.json  { LicenseKey, MachineId }
    public static (string? Key, string? MachineId) LoadStored() { ... }
    public static void SaveStored(string key, string machineId) { ... }

    // HWID: sorted MAC addresses (non-loopback, Up) + MachineName → SHA256 → first 32 hex chars
    // Uses: System.Net.NetworkInformation + System.Security.Cryptography (no new NuGet packages)
    public static string GetHwid() { ... }

    // Returns (ok, errorMessage in Thai)
    // Phase A: POST /licenses/actions/validate-key  (no auth header)
    // Phase B if NO_MACHINE: POST /machines  (Authorization: License {key})
    public static async Task<(bool ok, string? error)> ValidateAsync(
        string licenseKey, string machineId, CancellationToken ct = default) { ... }

    private static readonly HttpClient Http = new();
    private const string AccountId = "YOUR_KEYGEN_ACCOUNT_ID";
}
```

Keygen.sh `meta.code` handling:

| Code | Result |
|------|--------|
| `VALID` | `(true, null)` |
| `NO_MACHINE` / `NO_MACHINES` | activate machine (Phase B) → `(true, null)` |
| `FINGERPRINT_SCOPE_MISMATCH` | `(false, "ลิขสิทธิ์นี้ใช้งานกับเครื่องอื่น")` |
| `EXPIRED` | `(false, "ลิขสิทธิ์หมดอายุ")` |
| other | `(false, "กุญแจลิขสิทธิ์ไม่ถูกต้อง")` |

Phase B: `POST /accounts/{id}/machines`, `Authorization: License {key}`, body: `fingerprint` + `license.id` from Phase A response. HTTP 409 = already activated = success.

---

### 2. LicenseActivationWindow.axaml + .cs (new)

Modal `Window` (400×260). Thai UI.

```
StackPanel padding=32 spacing=16
  TextBlock  "กรุณากรอกรหัสลิขสิทธิ์"
  TextBox    Name=KeyBox  Watermark="XXXX-XXXX-XXXX-XXXX"
  TextBlock  Name=StatusText  Foreground=Red  IsVisible=False
  Button     "ยืนยัน"  Name=ConfirmBtn
  TextBlock  "กำลังตรวจสอบ..."  Name=LoadingText  IsVisible=False
```

On confirm: `ValidateAsync` → success: `SaveStored()`, `Activated = true`, `Close()`.
Caller: `var dlg = new LicenseActivationWindow(); await dlg.ShowDialog(owner); return dlg.Activated;`

---

### 3. SettingsWindow.axaml.cs (modify)

Constructor: `public SettingsWindow(bool isReadOnly = false)`

Promote button locals to fields:
```csharp
private Button _containerImportBtn = null!;
private Button _containerExportBtn = null!;
private Button _containerAddBtn    = null!;
private Button _containerSaveBtn   = null!;
private Button _productTemplateBtn = null!;
private Button _productImportBtn   = null!;
private Button _productExportBtn   = null!;
private Button _productAddBtn      = null!;
private Button _productSaveBtn     = null!;
private bool   _isReadOnly;
```

Add:
```csharp
public void ApplyReadOnly(bool readOnly)
{
    _isReadOnly = readOnly;
    foreach (var btn in new[] {
        _containerImportBtn, _containerExportBtn, _containerAddBtn, _containerSaveBtn,
        _productTemplateBtn, _productImportBtn, _productExportBtn, _productAddBtn, _productSaveBtn
    }) btn.IsEnabled = !readOnly;
}
```

Also check `_isReadOnly` in `BuildCardRow` / `BuildProductCard` for dynamically added rows.

---

### 4. MainWindow.axaml (modify)

Add as second child of root `DockPanel` (`DockPanel.Dock="Top"`):
```xml
<Border Name="ReadOnlyBanner" IsVisible="False"
        Background="#FFF3CD" Padding="12,6" DockPanel.Dock="Top">
  <TextBlock Text="โหมดอ่านอย่างเดียว — กรุณาเชื่อมต่ออินเทอร์เน็ตและเปิดใช้งานใหม่"
             Foreground="#856404" HorizontalAlignment="Center"/>
</Border>
```

---

### 5. MainWindow.axaml.cs (modify)

```csharp
private bool _isReadOnly;
private SettingsWindow? _settingsView;

public MainWindow(bool isReadOnly = false)
{
    InitializeComponent();
    SetReadOnly(isReadOnly);
}

public void SetReadOnly(bool value)
{
    _isReadOnly = value;
    ReadOnlyBanner.IsVisible = value;
    _settingsView?.ApplyReadOnly(value);
}
```

In `OpenSettings_Click`: `_settingsView ??= new SettingsWindow(_isReadOnly);`

---

### 6. App.axaml.cs (modify)

```csharp
public override void OnFrameworkInitializationCompleted()
{
    ContainerSpec.Load();
    ProductSpec.Load();

    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        var mainWindow = new MainWindow(isReadOnly: true);
        desktop.MainWindow = mainWindow;
        base.OnFrameworkInitializationCompleted();

        // Must run AFTER base() — queues onto the already-running UI dispatcher
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            bool ok = await RunLicenseCheckAsync(mainWindow);
            mainWindow.SetReadOnly(!ok);
        });
    }
}

private static async Task<bool> RunLicenseCheckAsync(MainWindow owner)
{
    var (key, machineId) = LicenseService.LoadStored();
    if (key is not null && machineId is not null)
    {
        var (ok, _) = await LicenseService.ValidateAsync(key, machineId);
        return ok;
    }
    var dlg = new LicenseActivationWindow();
    await dlg.ShowDialog(owner);
    return dlg.Activated;
}
```

> Never `.GetAwaiter().GetResult()` on UI thread — deadlock.

---

## Verification

1. First run: read-only → activation dialog → enter valid Keygen key → unlocks
2. Subsequent run (online): silent re-validate → full access
3. Subsequent run (offline): read-only banner, no dialog
4. HWID mismatch: copy `license.json` to another machine → Thai error, read-only

---

## Review

_To be filled after implementation._

---

# Todo #2 — Container & Product Data (Developer-Controlled in Phase 1)

## Context

Settings UI stays. But in Phase 1, container and product definitions are set by the developer — users can only VIEW them, not add/edit/delete/import/export. Phase 2 (after paying more): full user configuration unlocked.

Container and product data files move to a dedicated `logistic/Data/` folder.

**Phase 2 unlock**: re-enable the action buttons in `SettingsWindow` (already disabled by `ApplyReadOnly()` from Todo #1 — no extra work needed if Todo #1 is done first).

---

## Tasks

- [ ] 1. Create `logistic/Data/` folder with `containers.json` and `products.json` as the source-of-truth
- [ ] 2. Update `ContainerSpec.cs` — change load path to `logistic/Data/containers.json` (shipped with app, read-only to users)
- [ ] 3. Update `ProductSpec.cs` — change load path to `logistic/Data/products.json`
- [ ] 4. Keep `SettingsWindow` — it stays as-is (read-only enforced by Todo #1 `ApplyReadOnly()`)
- [ ] 5. Edit container/product data directly in `logistic/Data/*.json` (developer workflow, not user workflow)

---

## Spec

### Data folder structure

```
logistic/
  Data/
    containers.json   ← developer edits this before shipping
    products.json     ← developer edits this before shipping
```

### ContainerSpec.cs / ProductSpec.cs — path change

Currently loads from `%APPDATA%/logistic/containers.json` (user-writable). Change to load from the app's own directory (`AppContext.BaseDirectory`):

```csharp
private static readonly string FilePath = Path.Combine(
    AppContext.BaseDirectory, "Data", "containers.json");
```

This means:
- File ships alongside the `.exe` in the publish output
- Users cannot easily find or edit it (no `%APPDATA%` path)
- Developer edits `logistic/Data/containers.json` in the repo before building

### .csproj — include Data files in publish output

```xml
<ItemGroup>
  <Content Include="Data\*.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

### Settings UI

No changes needed. `ApplyReadOnly(true)` from Todo #1 disables all edit buttons already.

If Todo #1 is not yet done: temporarily hardcode `new SettingsWindow(isReadOnly: true)` in `MainWindow.axaml.cs`.

---

## Verification

1. Edit `logistic/Data/containers.json` → `dotnet run` → Settings page shows updated containers, no edit buttons
2. Publish output contains `Data/containers.json` next to the `.exe`
3. `%APPDATA%/logistic/` no longer contains `containers.json` or `products.json`
