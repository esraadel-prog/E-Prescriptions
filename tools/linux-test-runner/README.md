# مشغّل اختبارات بديل (Linux / .NET 8)

للتحقق السريع بدون Windows/Visual Studio/NuGet: بيترجم Core/Domain/Data الحقيقية + ملفات tests الحقيقية
فوق `libsqlite3` (بديل مصغّر لـ System.Data.SQLite) وبدائل صغيرة لـ xunit وFluentAssertions وCommunityToolkit وWPF.

    sudo apt-get install -y dotnet-sdk-8.0 libsqlite3-0
    cd tools/linux-test-runner
    dotnet build -c Release
    dotnet bin/Release/net8.0/runner.dll          # كل الاختبارات (أو مرّر جزءاً من الاسم كفلتر)

**مش بديل** عن `dotnet test` الحقيقي على Windows (خطوة 42 و45). `ReportingAndExportTests.cs` غير مشمول (يحتاج ClosedXML/PdfSharp).
