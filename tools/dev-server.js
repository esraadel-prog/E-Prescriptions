const http = require('http');

const html = `<!DOCTYPE html>
<html lang="ar" dir="rtl">
<head>
  <meta charset="utf-8">
  <title>e_prescriptions - نظام إدارة الصيدليات</title>
  <style>
    body { font-family: 'Segoe UI', Tahoma, Arial, sans-serif; background: #0f172a; color: #f8fafc; margin: 0; padding: 40px; display: flex; justify-content: center; align-items: center; min-height: 80vh; }
    .card { background: #1e293b; border: 1px solid #334155; border-radius: 16px; padding: 36px; max-width: 680px; width: 100%; box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5); }
    .badge { display: inline-block; background: #10b981; color: white; padding: 4px 12px; border-radius: 9999px; font-size: 13px; font-weight: bold; margin-bottom: 16px; }
    h1 { margin: 0 0 8px; font-size: 26px; color: #ffffff; }
    p { color: #94a3b8; line-height: 1.6; margin: 8px 0; }
    .stats { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; margin: 24px 0; }
    .stat { background: #0f172a; padding: 14px; border-radius: 10px; border: 1px solid #1e293b; text-align: center; }
    .stat-val { font-size: 22px; font-weight: bold; color: #38bdf8; }
    .stat-label { font-size: 11px; color: #64748b; margin-top: 4px; }
    .info { background: #1e1b4b; border: 1px solid #4338ca; border-radius: 8px; padding: 14px; margin-top: 18px; font-size: 13px; color: #c7d2fe; line-height: 1.6; }
    .tag { display: inline-block; background: #334155; color: #93c5fd; padding: 2px 8px; border-radius: 4px; font-size: 11px; margin: 2px; }
  </style>
</head>
<body>
  <div class="card">
    <span class="badge">Windows Desktop App (WPF / .NET Framework 4.8)</span>
    <h1>e_prescriptions — نظام إدارة الصيدليات</h1>
    <p>تطبيق سطح مكتب محلي متكامل لإدارة الصيدليات والوصفات الطبية مع قاعدة بيانات SQLite ومحزم Inno Setup 6.</p>
    
    <div class="stats">
      <div class="stat">
        <div class="stat-val">210</div>
        <div class="stat-label">اختبار آلي ناجح (100%)</div>
      </div>
      <div class="stat">
        <div class="stat-val">خطوة 34</div>
        <div class="stat-label">المرحلة المنجزة بنجاح</div>
      </div>
      <div class="stat">
        <div class="stat-val">CI/CD</div>
        <div class="stat-label">GitHub Actions نشط</div>
      </div>
    </div>

    <div style="margin: 16px 0;">
      <span class="tag">C# / WPF</span>
      <span class="tag">.NET 4.8</span>
      <span class="tag">SQLite</span>
      <span class="tag">Inno Setup 6</span>
      <span class="tag">xUnit</span>
      <span class="tag">POS & FEFO</span>
      <span class="tag">Smart Alerts</span>
    </div>

    <div class="info">
      <strong>حالة النظام:</strong> يتم بناء وحزم التطبيق عبر MSBuild و Inno Setup على بيئة Windows (GitHub Actions / Visual Studio) لإنتاج مثبت التثبيت المكتبي <code>e_prescriptions_Setup_v1.0.0.exe</code>.
    </div>
  </div>
</body>
</html>`;

const server = http.createServer((req, res) => {
  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(html);
});

const PORT = 3000;
server.listen(PORT, '0.0.0.0', () => {
  console.log(`Dev server dashboard listening on port ${PORT}`);
});
