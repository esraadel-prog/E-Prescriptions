-- 004_FinanceAndStaff.sql: المصروفات، الإيرادات، القيود اليومية، وإدارة الموظفين
-- جميع المبالغ المالية بالقروش/الهللات (INTEGER)

CREATE TABLE IF NOT EXISTS Expenses (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ExpenseNumber TEXT NOT NULL UNIQUE,
    Category TEXT NOT NULL, -- إيجار، كهرباء، رواتب، صيانة، مستلزمات، أخرى
    AmountHalalas INTEGER NOT NULL DEFAULT 0,
    Date TEXT NOT NULL,
    PaidTo TEXT NOT NULL,
    Notes TEXT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Incomes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    IncomeNumber TEXT NOT NULL UNIQUE,
    Source TEXT NOT NULL,
    AmountHalalas INTEGER NOT NULL DEFAULT 0,
    Date TEXT NOT NULL,
    Notes TEXT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS JournalEntries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EntryNumber TEXT NOT NULL UNIQUE,
    Date TEXT NOT NULL,
    Description TEXT NOT NULL,
    DebitAccount TEXT NOT NULL,
    CreditAccount TEXT NOT NULL,
    AmountHalalas INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_Expenses_Date ON Expenses(Date);
CREATE INDEX IF NOT EXISTS IX_Incomes_Date ON Incomes(Date);
CREATE INDEX IF NOT EXISTS IX_JournalEntries_Date ON JournalEntries(Date);
