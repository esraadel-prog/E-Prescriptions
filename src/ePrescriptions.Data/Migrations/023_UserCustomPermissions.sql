-- 023_UserCustomPermissions.sql: إضافة عمود الصلاحيات المخصصة للمستخدمين
-- يسمح بتحديد وتعديل الصلاحيات الفردية لأي موظف مستقلاً عن دوره العام
ALTER TABLE Users ADD COLUMN CustomPermissionsConfig TEXT NULL;
