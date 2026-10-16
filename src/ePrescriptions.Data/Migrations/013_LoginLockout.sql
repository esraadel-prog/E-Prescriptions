-- 013_LoginLockout.sql: قفل مؤقت للحساب بعد محاولات دخول فاشلة متتالية
ALTER TABLE Users ADD COLUMN FailedLoginCount INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Users ADD COLUMN LockedUntil TEXT NULL;
