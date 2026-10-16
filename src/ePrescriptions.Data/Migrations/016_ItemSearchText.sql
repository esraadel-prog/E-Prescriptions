-- 016_ItemSearchText.sql: عمود بحث موحّد مطبّع للأصناف (خطوة 24)
-- يُملأ من الكود (ArabicTextNormalizer) عند الحفظ، ويُعبّأ للأصناف القديمة بعد الـ Migration (NULL = لم يُفهرس بعد).
ALTER TABLE Items ADD COLUMN SearchText TEXT;
