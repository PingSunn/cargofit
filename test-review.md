# Test Review

## หมวด 1 — Engine: Packing Algorithm

เช็ค logic ของ `PackingEngine.cs` — การจัดวาง primary / condo / scatter

| Method | สิ่งที่เช็ค | สถานะ |
|--------|------------|-------|
| `SingleProduct_Aloe365ml_20ft` | Single product มี pattern, bounds + HasPattern | ✅ |
| `NoPattern_GumiJelly150G_ProducesZeroPlacements` | Product ไม่มี pattern → 0 placements | ✅ |
| `MultiProduct_AloeAndMogu320_20ft` | 2 products + condo — bounds + packed ≤ req | ✅ |
| `LargeQty_MoguCandy_40ft` | qty ×2000, H얇 — LayerIndex ไม่เกิน MaxLayers | ✅ |
| `TallBoxes_Mogu1000ml_40HC` | กล่องสูง ใน HC — bounds | ✅ |
| `SmallQty_MoguTea300ml_20ft` | qty น้อย (×50) ทุกลังต้องเข้าได้ | ✅ |
| `Aloe365ml_500boxes_20ft` | Aloe ×3000 ตู้ยาว — bounds | ✅ |
| `RealisticLoad_ThreeProducts_40ft` | 3 products จริง ตู้ยาว | ✅ |
| `FourProducts_RemainingY_40ft` | 4 products ตู้ยาว + per-stack height log | ✅ |
| `DevPreset_Mogu1000P12_Mogu320P24_20ft` | DevPreset 540+850 ลัง ตู้สั้น | ✅ |
| `MultiProduct_Aloe365P24_Aloe1000ML_20ft` | Aloe 365+1000ML ×300 each | ✅ |

## หมวด 2 — Engine: Scatter Phase

| Method | สิ่งที่เช็ค | สถานะ |
|--------|------------|-------|
| `Scatter_PlacesLeftoverOnSameProductStacks_DevPreset` | A-on-A rule: scatter ห้ามวางบน stack ของ product อื่น | ✅ |
| `Scatter_SingleProduct_FitsExactlyAfterPrimary` | Scatter single product — Z ไม่เกิน ceiling | ✅ |

## หมวด 3 — Engine: Condo Phase

| Method | สิ่งที่เช็ค | สถานะ |
|--------|------------|-------|
| `Condo_ColumnHeight_MatchesAdjacentPrimary` | Condo TopZ ≤ primary stack ที่ติดกัน (±1 layer) | ✅ |
| `Condo_InsufficientLeftover_GoesToScatterNotCondo` | leftover น้อย → ไม่เข้า condo | ✅ |

## หมวด 4 — Engine: Geometry / Invariants

| Method | สิ่งที่เช็ค | สถานะ |
|--------|------------|-------|
| `StackIndex0_IsInnermostPerProduct_Y0IsBackWall` | SI=0 อยู่ Y ต่ำสุด + condo ที่ Y=0 | ✅ |
| `SingleProduct_Mogu220MLP24_1800_Leaves100cmAtDoor_20ft` | door free ≤ 50 cm | ❌ FAIL (100 cm) |
| `SingleProduct_Mogu220MLP24_2240_DoorFreeSpaceUnder50cm_20ft` | door free ≤ 50 cm | ✅ |

## หมวด 5 — Engine: Fuzz / Random

| Method | สิ่งที่เช็ค | สถานะ |
|--------|------------|-------|
| `Random_100Cases_BasicInvariants` | 100 cases สุ่ม — bounds + packed ≤ req | ❌ FAIL case 096 Y overflow |

## หมวด 6 — Export: PDF

| Method | สิ่งที่เช็ค | สถานะ |
|--------|------------|-------|
| `Generate_FourProducts40HC_ProducesNonEmptyPdf` | PDF ไม่ว่าง (smoke test เท่านั้น) | ✅ |

---

## ควรมีเพิ่มอีก

### Engine — StatsCalculator *(ยังไม่มี)*
`StatsCalculator.cs` เป็น pure static class คำนวณ CBM + per-product stats จาก PackingOutput
- CBM ตรงกับผลรวม BW×BL×BH ของ placements
- Primary / Condo / Scatter count ตรงกับ StackIndex ranges

### Models — ContainerSpec / ProductSpec *(ยังไม่มี)*
- `InteriorW/L/H` = nominal − Gap ทุกด้าน
- Load/save `containers.json` roundtrip
- Load/save `products.json` + CSV import roundtrip
- `LayerSection` ที่มี `SubRows` แทน Rows/Cols/Rotated

### Canvas — IsometricProjection *(ยังไม่มี)*
Pure math ไม่มี UI dependency ทดสอบง่าย
- `Project(0,0,0)` ต้องได้ origin ที่ถูกต้องตาม azimuth/elevation
- `GetCorners` ครบ 8 จุด + ไม่เกิน bounding box ของกล่อง

### Export — PDF Content *(ยังไม่มี)*
ตอนนี้มีแค่ smoke test (ไฟล์ไม่ว่าง) ควรเพิ่ม:
- จำนวน product card ใน PDF ตรงกับ input
- Loading sequence มีทุก layer

### CLI — CliRunner *(ยังไม่มี)*
`CliRunner.cs` ~400 บรรทัด ยังไม่มี test เลย
- parse argument ถูก
- output JSON/text ตรงกับ PackingEngine

### Utils — AppPaths *(ยังไม่มี)*
- dev mode: หา `.git` แล้ว return repo root
- release mode: return `%LocalAppData%/CargoFit/`
