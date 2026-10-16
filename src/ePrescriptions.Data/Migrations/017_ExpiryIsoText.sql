-- 017_ExpiryIsoText.sql: تاريخ انتهاء التشغيلة يُخزَّن ويُقارَن كنص ISO (yyyy-MM-dd) مباشرة (خطوة 25)
-- 1) قيم قديمة بصيغة ISO فيها وقت ("2027-01-31T00:00:00" أو "2027-01-31 00:00:00") تُقصّ لـ 10 خانات.
--    (أي صيغ تانية غير ISO بتتصلّح من الكود بعد الـ Migration عبر DatabaseContext.NormalizeLegacyExpiryDates)
UPDATE ItemBatches
SET ExpiryDate = substr(trim(ExpiryDate), 1, 10)
WHERE length(trim(ExpiryDate)) > 10
  AND substr(trim(ExpiryDate), 5, 1) = '-' AND substr(trim(ExpiryDate), 8, 1) = '-';

-- 2) حماية على مستوى القاعدة: أي كتابة لـ ExpiryDate بغير yyyy-MM-dd تُرفض (عشان المقارنة النصية تفضل صحيحة)
CREATE TRIGGER IF NOT EXISTS TR_ItemBatches_ExpiryIso_Insert
BEFORE INSERT ON ItemBatches
WHEN NEW.ExpiryDate IS NULL OR NEW.ExpiryDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
BEGIN
    SELECT RAISE(ABORT, 'ExpiryDate must be ISO yyyy-MM-dd');
END;

CREATE TRIGGER IF NOT EXISTS TR_ItemBatches_ExpiryIso_Update
BEFORE UPDATE OF ExpiryDate ON ItemBatches
WHEN NEW.ExpiryDate IS NULL OR NEW.ExpiryDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
BEGIN
    SELECT RAISE(ABORT, 'ExpiryDate must be ISO yyyy-MM-dd');
END;
