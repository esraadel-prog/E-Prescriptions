-- 012_NumberCounters.sql: عدادات ترقيم المستندات (بتتحدّث داخل نفس الـ Transaction اللي بتحفظ المستند)
CREATE TABLE IF NOT EXISTS Counters (
    Name TEXT PRIMARY KEY,
    Value INTEGER NOT NULL DEFAULT 0
);
