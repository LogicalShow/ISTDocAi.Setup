# تشغيل أداة ISTDocAi.OperatorTool

1. **مين يشغّلها؟** مشغّل IST بحساب Windows مصرح له على قاعدة DocAi. افتح Terminal عادي؛ الأداة `asInvoker` ومش محتاجة elevation. إدارة الـ logins محتاجة صلاحية مسؤول SQL، مش مسؤول Windows. شغّل `ISTDocAi.OperatorTool.exe --help` للتفاصيل. الخروج: 0 نجاح، 1 رفض، 2 إجراء مطلوب، 3 إلغاء.

2. **الاتصال:** حدّد `--sql-server SQL01 --database ISTDocAi` أو سيب الأداة تسألك. الاتصال Windows Authentication وتشفيره Mandatory. `TrustServerCertificate` افتراضيًا no؛ استخدم شهادة موثوقة، أو اختار `--trust-server-certificate` صراحةً لو مسؤول SQL طلب ده. مفيش connection strings أو مفاتيح بتتطبع.

3. **الرصيد والشحن:** الدور المطلوب `docai_quota_operator`.
   ```powershell
   ISTDocAi.OperatorTool.exe pages status --consumer FWD-UNF
   ISTDocAi.OperatorTool.exe pages topup --consumer FWD-UNF --pages 100 --note 'Approved allowance' --reference ORDER-100
   ISTDocAi.OperatorTool.exe pages set-mode --consumer FWD-UNF --mode Prepaid --top-up-pages 100 --note 'Initial prepaid allowance'
   ISTDocAi.OperatorTool.exe pages set-warning --consumer FWD-UNF --percent 10 --note 'Approved warning threshold'
   ISTDocAi.OperatorTool.exe pages reverse --consumer FWD-UNF --grant-id 123 --note 'Approved reversal'
   ```
   احفظ `RequestId` اللي بيظهر قبل الاتصال. المعاينة بتتراجع تلقائيًا؛ راجع Before → After وRemaining وStatus وبعدها اكتب `yes`. لو الرصيد اتغير، لازم تعاين تاني. عند انقطاع الاتصال، افحص status وأعد نفس الأمر بنفس `--request-id GUID` ونفس البيانات؛ مفيش شحن مكرر. نفس الرقم ببيانات مختلفة بيتِرفض. `adjust --pages -10` لتسوية معتمدة؛ `--allow-debt` تصريح صريح بالرصيد السالب. `--confirm-blocked` في تحويل Prepaid من غير رصيد محتاج تأكيد إضافي لأنه بيوقف العميل فورًا. الشحن مع التحويل في transaction واحدة هو الاختيار الموصى به.

4. **التقارير:** افتراضيًا كشف عميل بصفحات فقط، بدون أسعار أو تكلفة. دور quota يقرأ من SQL؛ API العميل بيتحقق بمفتاح الكيت.
   ```powershell
   ISTDocAi.OperatorTool.exe pages cycles --consumer FWD-UNF --json
   ISTDocAi.OperatorTool.exe pages documents --consumer FWD-UNF --grant-id 0 --limit 100 --csv D:\reports\prelude.csv
   ISTDocAi.OperatorTool.exe pages cycles --consumer FWD-UNF --grant-id 123 --pdf D:\reports\statement.pdf
   ISTDocAi.OperatorTool.exe report customer --base-url https://docai.example.org --kit-secrets D:\Kit\App_Data\docai.secrets.config --grant-id prelude --documents --csv D:\reports\documents.csv
   ```
   اعمل فولدر التقارير الأول. الملفات الموجودة مابتتستبدلش إلا مع `--force`. SQL Documents مع CSV صفحة واحدة؛ استخدم `--after-charge-id` للصفحة التالية. API مع CSV وPDF بيتابع الصفحات لحد 10000 صف ويحذر لو فيه باقي. Grant 0 في SQL أو `prelude` في API = قبل أول شحن. Cycles من غير grant تعرض آخر 200 فترة مع إجماليات كل التاريخ. الشحن المعكوس يفضل حد للفترة؛ الترتيب timestamp ثم grants قبل charges ثم identifier، مش FIFO. كل صفحة snapshot مستقل؛ الاستهلاك المتزامن ممكن يغيّر الإجماليات. المحجوز مش استهلاك نهائي، وإعادة قراءة المستند ممكن تتحسب تاني.

5. **تقرير IST الداخلي:** حدّد `--audience ist` صراحةً مع pages cycles/documents. فيه تقديرات Azure بالدولار USD وعبارة **IST internal — do not send to the customer**. **ممنوع تبعته للعميل.** فاتورة Azure هي المرجع النهائي؛ الصفوف اللي مالهاش سعر مش داخلة في التقدير وبتطلع تحذير. `report customer` دايمًا كشف عميل. الاتصال بالـ API لازم HTTPS origin بشهادة موثوقة، بدون redirects؛ timeout 30 ثانية ومحاولات إضافية بحد أقصى 3 على 429/503 مع Retry-After. المفتاح بيتقرأ من XML ومش بيتبعت للـ renderer. PDF محتاج Arial: `arial.ttf` و`arialbd.ttf`؛ غيابهم يرجّع ACTION NEEDED. المكتبة ثابتة PDFsharp-MigraDoc 6.2.4؛ ممنوع ترقيتها تلقائيًا.

6. **الموديلات:** شغّل بعد اكتمال migrations وبصلاحية `docai_modelteam`. استخدم دقة اختبار حقيقية وملفات mapping كاملة مراجعة وفيها `ChangeReference`.
   ```powershell
   ISTDocAi.OperatorTool.exe models register-set --bl-model-id bl-v3 --bl-test-field-accuracy 0.97 --bl-mapping-file reviewed-bl.json --manifest-model-id manifest-v3 --manifest-test-field-accuracy 0.92 --manifest-mapping-file reviewed-manifest.json --classifier-model-id router-v3 --classifier-test-field-accuracy 0.91
   ISTDocAi.OperatorTool.exe models register-bl --model-id bl-v4 --test-field-accuracy 0.98 --mapping-file reviewed-bl.json
   ISTDocAi.OperatorTool.exe models mapping --model-id manifest-v3 --document-type-code MANIFEST --mapping-file reviewed-manifest.json
   ```
   راجع SQL المعروض وبعدها `yes`. المجموعة الكاملة بتتسجل وتتفعّل في transaction واحدة؛ أي فشل بيرجع المجموعة كلها. BL افتراضيًا minimum accuracy 0.95، والـ router 0.6. الموديل Active وإعادة نفس الـ mapping مابتغيّرش البيانات؛ المحتوى المختلف بيتِرفض. التراجع للـ BL يتم بـ register-bl ورقم الموديل القديم والدقة المقاسة؛ الربط القديم بيفضل محفوظ. تغيير mapping محتاج ModelId جديد، مش تعديل profile موجود.

7. **صلاحيات المشغّلين ومسار IT:**
   ```powershell
   ISTDocAi.OperatorTool.exe operators --operator-group 'DOMAIN\DocAiOperators' --modelteam-group 'DOMAIN\DocAiModels' --quota-operator-group 'DOMAIN\DocAiQuota'
   ISTDocAi.OperatorTool.exe pages topup --consumer FWD-UNF --pages 100 --note 'Approved allowance' --generate-sql D:\it-sql
   ISTDocAi.OperatorTool.exe --export-sql D:\templates
   ```
   operators ينشئ Windows logins/users وعضويات الأدوار الثلاثة فقط. مفيش منح runtime أو db_owner. أوامر SQL تقبل `--generate-sql FOLDER` بدون اتصال؛ الملفات الموجودة بتترفض. في أوامر الرصيد اللي بتغيّر البيانات، مسؤول IT يشغّل `<RequestId>-preview.sql`، يراجع النتيجة، وينقل PREVIEW_TOKEN إلى `@ReviewedPreviewToken` ويضبط `@OperatorConfirmed=1` في ملف apply. أعِد نفس الملف عند retry. models بتنتج `05-register-models.sql` أو `05-register-model.sql` أو `05-register-model-mapping.sql`؛ operators ينتج `04-operators.sql`. `--export-sql` يصدر القوالب المرجعية بدون تغيير أو استبدال.
# أوامر Azure

المشغّل محتاج صلاحيات إنشاء app registrations وservice principals وتعيين الأدوار على الموارد والمجموعة اللي فيها Document Intelligence. تسجيل الدخول بيفتح المتصفح؛ مفيش احتياج لـAzure CLI أو PowerShell في تشغيل المنتج. البرنامج بيعرض الحساب والـtenant قبل أي تغيير.

```text
ISTDocAi.OperatorTool.exe azure identity create --tenant TENANT_GUID --env dev
ISTDocAi.OperatorTool.exe azure infra --tenant TENANT_GUID
```

الإنشاء بيطلب كلمة سر PFX مرتين بشكل مخفي، ومجلد خاص خارج أي repository؛ الافتراضي `%LOCALAPPDATA%\DocAi\backups`. ملف JSON جنب الـPFX بنفس الاسم، وبيستخدمه ServerSetup وDevSetup. صلاحيات ملف الـPFX للمستخدم الحالي وSYSTEM وAdministrators بس. ابعت الـPFX لمسؤول IT وكلمة السر من قناة مختلفة. الاسم الموجود بيترفض؛ استخدم `--client-id GUID` بس لو قاصد تضيف شهادة للتطبيق الموجود.

`azure infra` بيعرض الاشتراكات ويطلب resource group والموقع (الافتراضي francecentral)، dev أو prod، هوية التطبيق أو service principal object id، اسم ومجموعة DI الموجود، مجموعات SQL والمطورين، التنبيهات والميزانية وتاريخ بدايتها وpurge protection. وضع الاستضافة ثابت `onPremises`. راجع ملخص what-if قبل التأكيد؛ الموارد لها تكلفة. الميزانية للتنبيه ومش سقف للصرف. تفعيل purge protection نهائي. البرنامج بيحفظ outputs JSON بدون مفاتيح في المسار اللي تختاره؛ استخدمه مع ServerSetup `package`. لو المجموعة مش موجودة، إنشاء المجموعة له تأكيد منفصل حتى مع `--yes`.

التجديد السنوي قبل انتهاء الشهادة بـ30 يوم على الأقل:

```text
ISTDocAi.OperatorTool.exe azure identity renew --tenant TENANT_GUID --client-id CLIENT_GUID
ISTDocAi.ServerSetup.exe identity-renew
ISTDocAi.ServerSetup.exe check
ISTDocAi.OperatorTool.exe azure identity remove-old --tenant TENANT_GUID --client-id CLIENT_GUID --thumbprint OLD_HEX
```

شغّل `identity-renew` و`check` على السيرفر من Terminal كمسؤول، وراجع `/health/ready` وجرّب استخراج جديد قبل `remove-old`. التجديد بيحتفظ بالشهادات العامة الموجودة، والسيرفر بيحتفظ بالشهادة القديمة. ما تشيلش الشهادة اللي السيرفر لسه بيستخدمها؛ آخر credential ما ينفعش يتشال. نفّذ تغييرات credentials بالتتابع من مشغّل واحد: البرنامج بيرفض أي تغيير متزامن رصده قبل PATCH، لكن Graph مش بيوفر transaction للخطوات دي. لو الاتصال اتقطع، راجع حالة Azure وملفات التصدير قبل إعادة المحاولة؛ البرنامج مش بيعيد POST تلقائيًا.

`--yes` بيتخطّى التأكيد النهائي فقط؛ مش بيتخطّى الأسئلة أو فحص كلمات السر أو تأكيد إنشاء resource group. في التجديد اسم ملفات التصدير بيشمل thumbprint علشان يحتفظ بالنسخة القديمة. الملفات الموجودة ما بيتكتبش فوقها.

