# ISTDocAi.Setup

أدوات تجهيز ISTDocAi والكيت. الـ repo ده منفصل عن ISTDocAi وعن الكيت. البرامج بتسأل عن المسارات وهي شغالة، فتقدر تعمل clone لأي واحد فيهم في أي مكان.

## البرامج

| البرنامج | مين بيشغّله | الدليل |
|---|---|---|
| `ISTDocAi.DevSetup.exe` | المطوّر على جهازه | [خطوات جهاز المطوّر](docs/DEV-SETUP-AR.md) |
| `ISTDocAi.ServerSetup.exe` | IT على سيرفر DocAi عند IST | [تثبيت سيرفر DocAi](docs/SERVER-SETUP-AR.md) |
| `ISTDocAi.KitSetup.exe` | IT على سيرفر الكيت عند العميل | [تجهيز سيرفر الكيت](docs/KIT-SETUP-AR.md) |
| `ISTDocAi.OperatorTool.exe` | مشغّل IST: الرصيد والتقارير والموديلات و Azure | [أوامر المشغّل](docs/OPERATOR-AR.md) |

- الـ SQL اللي كل برنامج محتاجه موجود جواه. `--export-sql FOLDER` بيطلّعه لمسؤول الداتابيز.
- `DevSetup` بيطلب صلاحية المسؤول لوحده.
- `ServerSetup` و `KitSetup` بيتشغّلوا من Terminal مفتوح كمسؤول. `--help` و `status` بيشتغلوا من غير صلاحية المسؤول.
- `OperatorTool` بيشتغل بصلاحيات حسابك العادي. أوامر Azure بتفتح المتصفح عشان تسجّل دخول، ومش محتاجة Azure CLI. الحساب لازم يكون عنده صلاحية إنشاء app registrations و role assignments.

## البناء والـ tests

استخدم الـ SDK المحدد في `global.json`. الـ tests بتحتاج ISTDocAi: حطه جنب الفولدر ده، أو عرّف `ISTDOCAI_ROOT` بمساره.

```powershell
$env:ISTDOCAI_ROOT = 'D:\unifret\ISTDocAi'
dotnet restore ISTDocAi.Setup.sln --locked-mode
dotnet build ISTDocAi.Setup.sln -c Release --no-restore -warnaserror
dotnet test tests/ISTDocAi.Setup.Tests/ISTDocAi.Setup.Tests.csproj -c Release --no-build --no-restore --filter "TestCategory!=Sql&TestCategory!=Machine" --logger trx --results-directory TestResults/ci
python .github/scripts/check-trx.py TestResults/ci
```

لتشغيل tests الـ SQL، عرّف `DOCAI_TEST_SQL` بـ connection string لـ SQL Server، وهي بتعمل داتابيزات مؤقتة وتمسحها.

## النشر (exe واحد لكل برنامج)

```powershell
dotnet publish src/ISTDocAi.DevSetup -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/DevSetup
dotnet publish src/ISTDocAi.ServerSetup -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/ServerSetup
dotnet publish src/ISTDocAi.KitSetup -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/KitSetup
dotnet publish src/ISTDocAi.OperatorTool -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/OperatorTool
```

## ملاحظات
- **البنية الأساسية على Azure:** ملفات bicep في `azure/infra`. لو عدّلت `main.bicep`، شغّل `bicep build` عشان يتحدّث `main.json`. الـ test المسؤول عن ده من نوع Machine.
- **مكتبة الـ PDF:** PDFsharp-MigraDoc مثبّتة على 6.2.4 بترخيص MIT. ما ترفعهاش، لأن الإصدارات الأحدث بقت AGPL أو تجارية.
- **الـ CI:** محتاج variable اسمه `ISTDOCAI_REPOSITORY` بقيمة `owner/repository`، و secret اسمه `ISTDOCAI_READ_TOKEN` يقدر يقرا ISTDocAi.
- **السكربتات القديمة:** اتنقلت لـ `D:\unifret\ISTDocAi-NonCode\setup-legacy-2026-11-14`، وكل الملفات اللي اتنقلت متسجلة في `MOVED-2026-11-14.txt`.
