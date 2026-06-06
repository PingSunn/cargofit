# Interlock Designer — Status

อัปเดต: 2026-06-07
Branch: `feat/interlock-designer` (merge เข้า `fix/arrangement`)

## คอนเซ็ปต์หลัก
- **1 สินค้า = 1 Pattern ใช้ได้ทุกตู้** (ทุกตู้กว้าง 235 เท่ากัน) — ตู้ต่างกันแค่ ยาว/สูง
- ตู้ใช้แค่ **รายงานจำนวน** ("ใส่ได้กี่ลัง/ตู้") = ลัง/ชั้น × ชั้นสูง(floor H/boxH) × ชั้นลึก(floor L/depth) — ไม่เปลี่ยน pattern

## ทำเสร็จแล้ว ✅
- **Engine** [`CargoFit/Engine/InterlockDesigner.cs`](CargoFit/Engine/InterlockDesigner.cs)
  - `Generate(w,l,h, containerW=235, containerL=0, containerH=0)` → `InterlockResult`
  - ค้นหา candidate: **two-band split** (แถบตั้ง + แถบหมุน, depth-matched) + **pinwheel** (กล่องเกือบจตุรัส) + grid (fallback)
  - คะแนน: **ขัดก่อน** (interlock-first) → ใช้พื้นที่มากสุด → จตุรัส → ตื้น
  - depth cap = 3.2 × ด้านยาวสุด
  - `PatternB` = สลับลำดับ band (ขัดระหว่างชั้น); pinwheel → B ว่าง
  - containerL/H = แค่คำนวณ total ของ pattern เดิม (ไม่เปลี่ยน pattern)
- **UI** [`CargoFit/Views/InterlockDesignerView.cs`](CargoFit/Views/InterlockDesignerView.cs)
  - โหมดใหม่ระดับบน "ออกแบบการเรียง" (nav ใน [`MainWindow`](CargoFit/Views/MainWindow.axaml.cs))
  - free-form: กรอก W×L×H + ความกว้างตู้ → กด คำนวณ → top view ของ Layer A/B + ตัวเลข
  - โชว์ **ใส่ได้/ตู้ ทุกขนาดพร้อมกัน** (pattern เดียวกัน)
  - ปุ่ม "บันทึก PatternA/B" ลง products.json ได้
- **Engine helpers** ใน [`PackingEngine.cs`](CargoFit/Engine/PackingEngine.cs): `LayerBoxCount`, `LayerPlacements` (internal — ใช้ score/วาด preview ด้วย logic วางกล่องตัวจริง)
- **Tests** 23 ผ่าน — [`CargoFit.Tests/Engine/InterlockDesignerTests.cs`](CargoFit.Tests/Engine/InterlockDesignerTests.cs)
  - fit width, engine นับตรง, ขัดจริง, near-square→pinwheel, B=cross ของ A, **pattern เท่ากันทุกตู้**
- **Proof HTML** [`CargoFit.Tests/InterlockProofGen.cs`](CargoFit.Tests/InterlockProofGen.cs) → `interlock-proof.html` (เทียบ pattern มือ vs auto ทุก SKU)

## ผลลัพธ์
- auto **≥ มือ ทุก SKU** (ดู interlock-proof.html) — เท่ากันส่วนใหญ่, ดีกว่าหลายตัว (Mogu Ice 150ML 70%→100%, Mogu 500 79%→97%), และสร้างให้ตัวที่มือไม่มี (Mogu Jelly Korea)

## ยังไม่ทำ / มาต่อ ⏳
- [ ] **ลายฟันปลา (sub-row middle)** แบบ Gumi 320 มือ — ตอนนี้ auto ได้ density เท่ากันแต่ออกมาเป็น two-band; เพิ่ม generator ถ้าอยากได้หน้าตาเป๊ะ
- [ ] **Feature 2: ลาก/แก้กล่องเศษ** ในมุมมอง 2D (top/layer) — ยังไม่เริ่ม (ดูแผนใน plan file)
- [ ] (optional) 3D preview ในหน้า designer
- ตัดทิ้งแล้ว: depth-aware เลือก pattern ต่างกันต่อตู้ (ขัดหลัก "1 pattern ทุกตู้")

## รัน / เทสต์
```bash
dotnet run --project CargoFit/CargoFit.csproj          # → แท็บ "ออกแบบการเรียง"
dotnet test CargoFit.Tests/CargoFit.Tests.csproj        # 23 tests + regen interlock-proof.html
open interlock-proof.html                                # เทียบ มือ vs auto
```

## หมายเหตุ git
- มี stash ค้าง: **"WIP fix/arrangement before interlock-designer"** (งานเก่า PackingEngine ~325 บรรทัด) — `git stash list` / `git stash pop` ตอนกลับมาทำ fix/arrangement ต่อ (อาจ conflict กับ helper ที่เพิ่งเพิ่มใน PackingEngine.cs)
