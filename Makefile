.PHONY: run build test run-test run-gui

# รัน GUI ปกติ (ไม่มี preset)
run:
	dotnet run --project CargoFit/CargoFit.csproj

# รัน GUI พร้อม preset — copy ไฟล์ไปที่ root ก่อน แล้ว GUI จะโหลดอัตโนมัติ
# Usage: make run-gui FILE=testdata/devpreset.json
run-gui:
ifndef FILE
	$(error กรุณาระบุ FILE เช่น: make run-gui FILE=testdata/devpreset.json)
endif
	cp $(FILE) devpreset.json
	dotnet run --project CargoFit/CargoFit.csproj

# Build เฉยๆ
build:
	dotnet build

# รัน tests ทั้งหมด
test:
	dotnet test CargoFit.Tests/CargoFit.Tests.csproj --logger "console;verbosity=detailed"

# รัน packing engine แบบ headless ด้วย JSON fixture
# Usage: make run-test FILE=testdata/devpreset.json
run-test:
ifndef FILE
	$(error กรุณาระบุ FILE เช่น: make run-test FILE=testdata/devpreset.json)
endif
	dotnet run --project CargoFit/CargoFit.csproj -- --input $(FILE)
