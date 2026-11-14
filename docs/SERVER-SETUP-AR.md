# تثبيت DocAi على سيرفر IST

## هتستلم إيه
- فولدر الباكدج، وفيه `ISTDocAi.ServerSetup.exe` و `server-settings.json` وفولدر `app`.
- شهادة الهوية (PFX). كلمة سرها هتوصلك لوحدها، مش مع الشهادة.

## قبل ما تبدأ
- شهادة HTTPS متثبتة على السيرفر وصالحة وفيها private key.
- اسم الموقع (DNS) متجهّز.
- عندك صلاحية على SQL Server، أو مسؤول الداتابيز جاهز يشغّل ملف SQL.

## الخطوات
افتح Terminal **كمسؤول** جوه فولدر الباكدج، ونفّذ الخطوات دي بالترتيب:

1. **التثبيت:**
   ```
   ISTDocAi.ServerSetup.exe install
   ```
   هيسأل عن:
   - اسم الموقع، والـ host، والبورت.
   - شهادة HTTPS: هيعرضلك قائمة وتختار منها رقم.
   - فولدر التثبيت.
   - حساب الـ pool. لو SQL على جهاز تاني، لازم يبقى حساب دومين.
   - اسم الـ SQL Server والداتابيز.
   - مسار الـ PFX وكلمة سره.

   كلمات السر مش بتظهر وإنت بتكتبها.
2. **لما يسألك عن SQL login**، اختار رقم:
   - `1`: يشغّله هو بحسابك.
   - `2`: مسؤول الداتابيز شغّله خلاص، كمّل.
   - `3`: يكتب `sql\app-login.sql` لمسؤول الداتابيز ويقف. بعد ما المسؤول يشغّله، شغّل `install` تاني واختار `2`.
3. **تسجيل العميل:** خد الـ pairing code من مسؤول الكيت، وشغّل:
   ```
   ISTDocAi.ServerSetup.exe apply
   ```
   الصق الكود، ومش هيظهر وإنت بتلصقه. بعدها هيسألك يشغّل ملف التسجيل بنفسه ولا تديه لمسؤول الداتابيز.
4. **الفحص:**
   ```
   ISTDocAi.ServerSetup.exe check
   ```
   `/health/ready` ممكن يرجع 503 لحد ما تسجيل العميل يخلص.

## بعد كده
- **نسخة جديدة:** `ISTDocAi.ServerSetup.exe update`. الإعدادات بتفضل زي ما هي.
- **الإزالة:** `ISTDocAi.ServerSetup.exe remove`. بيشيل بس الحاجات اللي هو عملها. الداتابيز والشهادة والأسرار واللوجات بتفضل.

## لو حصلت مشكلة
- لو التثبيت وقف في النص: شغّل `install` تاني، هيكمّل من المكان اللي وقف فيه.
- ما تمسحش `C:\ProgramData\ISTDocAi\setup\state.json` بإيدك.
- لو ظهرت `ACTION NEEDED` أو `REFUSED`: اعمل اللي مكتوب. لو مش واضح، ابعت نص الرسالة لـ Anter.
- ما تبعتش الـ pairing code ولا أي كلمة سر في إيميل ولا شات.

## للمطوّر: تجهيز الباكدج
```
ISTDocAi.ServerSetup.exe package
```
بيتشغّل على جهاز فيه سورس ISTDocAi و .NET SDK. هيسأل عن:
- فولدر ISTDocAi.
- فولدر الإخراج، ولازم يكون فولدر جديد.
- ملف identity JSON.
- روابط Azure.

الباكدج مفيهوش أي PFX ولا كلمات سر.
# تجديد شهادة هوية Azure

بعد `OperatorTool azure identity renew`، انقل الـPFX الجديد وملف JSON اللي جنبه للسيرفر، واستلم كلمة السر من قناة مختلفة. افتح Terminal كمسؤول وشغّل:

```text
ISTDocAi.ServerSetup.exe identity-renew
ISTDocAi.ServerSetup.exe check
```

البرنامج بيقرأ install journal، وبيطلب الـPFX وJSON وكلمة السر بشكل مخفي. لازم tenant وclient id يطابقوا التثبيت والشهادة تطابق thumbprint في JSON. بعد التأكيد بيستورد الشهادة في `LocalMachine\My` من غير قابلية تصدير، ويدي pool identity صلاحية Read على المفتاح، ويغيّر قيمة `DocAi:Identity:CertificateThumbprint` بس مع الحفاظ على باقي بايتات `appsettings.Production.json`، ويعمل recycle للـpool.

بيفحص `/health/live` ويعرض `/health/ready`. لو أي فحص فشل، راجع السبب وأعد نفس التجديد؛ الـjournal بيسجل التجديد المعلّق والـthumbprint القديم، والشهادة القديمة بتفضل موجودة. بعد HTTP 200 من readiness وتجربة استخراج ناجحة، المشغّل ينفّذ `azure identity remove-old` للـthumbprint القديم. ما تحذفش القديم قبل نجاح التحقق. `--yes` بيتخطّى التأكيد النهائي بس.

