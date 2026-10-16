-- 009_SupplierReturnReason.sql: سبب مرتجع/شطب الأدوية للمورد (انتهاء صلاحية / تلف / ...)
ALTER TABLE SupplierReturns ADD COLUMN Reason TEXT NOT NULL DEFAULT '';
