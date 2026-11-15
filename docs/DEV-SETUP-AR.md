# تجهيز جهاز المطوّر لـ ISTDocAi والكيت

## هتستلم من Anter
| الملف | ملاحظة |
|---|---|
| `ISTDocAi.DevSetup.exe` | البرنامج اللي هيجهّز جهازك. |
| `docai-dev.pfx` و `docai-dev.json` | شهادة Azure. **خلّيهم جنب بعض في نفس الفولدر**، وما ترفعهمش على git ولا تبعتهم لحد. |
| كلمة سر الـ PFX | هتوصلك **لوحدها** في رسالة منفصلة. |
| `ISTDocAi-TestDocs.zip` | ملفات للتجربة. |

مش محتاج تعمل build لأي أداة من دول.

## قبل ما تبدأ
1. **ثبّت:**
   - Visual Studio 2026.
   - .NET 10 SDK، الإصدار 10.0.401 أو أحدث.
   - SQL Server.
2. **اعرف اسم الـ SQL Server عندك.** اكتبه زي ما هو مكتوب في SSMS في خانة Server name بالظبط.
   - مش متأكد؟ شغّل في PowerShell: `Get-Service -Name 'MSSQL*'`
   - لو ظهر `MSSQLSERVER` يبقى تكتب **`.`** (نقطة بس). ما تكتبش `localhost`: على SQL Server الجديد بيكون TCP/IP مقفول، و`localhost` مش هتتوصل.
   - لو ظهر `MSSQL$SQLEXPRESS` يبقى تكتب `.\SQLEXPRESS`، وكده مع أي اسم تاني.
3. **اعمل clone للاتنين في أي مكان على جهازك:**
   - repo ISTDocAi، والرابط هتاخده من Anter.
   - الكيت على branch اسمه `integration/docai-front`.
4. **اقفل الكيت** لو مفتوح في Visual Studio.

## التجهيز
1. افتح `ISTDocAi.DevSetup.exe` بدبل كليك.
   - لو ظهرت رسالة **"Windows protected your PC"**، دوس **More info** وبعدين **Run anyway**.
   - هيطلب صلاحية المسؤول، دوس **Yes**.
2. جاوب على الأسئلة:

   | السؤال | تكتب إيه |
   |---|---|
   | `ISTDocAi folder` | مسار فولدر ISTDocAi اللي فيه `ISTDocAi.sln` |
   | `Kit folder` | مسار فولدر `ForwardingUniFreight.Solution` أو `Forwarding.MvcApp` |
   | `Continue?` | `yes` |
   | `SQL Server [...]` | اسم الـ SQL Server بتاعك. **ما تدوسش Enter على القيمة الافتراضية**، دي اسم السيرفر عند Anter |
   | `PFX path` | مسار `docai-dev.pfx` |
   | `PFX password` | كلمة السر. مش هتظهر وإنت بتكتبها، ودي حاجة طبيعية |

   تقدر تسحب الفولدر أو الملف من File Explorer وتسيبه جوه الشاشة السودا بدل ما تكتب المسار.
3. **أول مرة هيقف ويقولك:**
   `ACTION NEEDED: Start ISTDocAi once from Visual Studio (profile ISTDocAiLocal)...`

   ودي حاجة طبيعية، لأن الداتابيز بتاعة ISTDocAi لسه ما اتعملتش. اعمل كده:
   1. افتح `ISTDocAi.sln` في Visual Studio.
   2. اختار profile **ISTDocAiLocal**، ودوس **F5**.
   3. استنى دقيقة لحد ما يقوم. هو بيعمل الداتابيز لوحده.
   4. اقفله.
   5. افتح `ISTDocAi.DevSetup.exe` تاني وجاوب نفس الأسئلة. المرة دي مش هيسألك عن الـ PFX تاني.
4. **في الآخر لازم تشوف:**
   ```
   DONE development seed (opening pages added)
   DONE kit credentials
   OK Company database ... is FWD-UNF
   Restart ISTDocAi and the kit, then test Extract with AI
   ```
   دوس **Enter** عشان تقفل.

## التجربة
1. شغّل ISTDocAi بـ F5 على profile **ISTDocAiLocal**.
2. افتح الكيت من الـ branch `integration/docai-front`، وشغّله بـ F5.
3. فك `ISTDocAi-TestDocs.zip`، وجرّب **Extract with AI** على B/L أو Manifest منهم.

## لو حصلت مشكلة
- **`ACTION NEEDED`:** اعمل اللي مكتوب فيها، وافتح الـ exe تاني. الـ exe بيكمّل من غير ما يكرر اللي خلص.
- **`REFUSED`:** وقّف، وابعت نص الرسالة لـ Anter.
- **`PFX password is wrong`:** اتأكد من كلمة السر واكتبها تاني.
- **`Cannot connect to SQL Server`:** اتأكد من اسم الـ SQL Server وإنه شغال. لو كتبت `localhost` جرّب **`.`** بدلها (أو `.\اسم_الـinstance`).
- **ما تستخدمش `--rotate`** إلا لو Anter قالك.
