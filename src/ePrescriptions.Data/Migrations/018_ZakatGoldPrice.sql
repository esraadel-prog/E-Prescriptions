-- 018_ZakatGoldPrice.sql: سعر جرام الذهب المحفوظ لحاسبة الزكاة (خطوة 29)
-- جدول منفصل (مش عمود في PharmacySettings) لأن SaveSettings بيستخدم INSERT OR REPLACE وكان هيمسح السعر.
-- مفيش صف = السعر لسه ما اتدخلش (مفيش قيمة افتراضية).
CREATE TABLE IF NOT EXISTS ZakatSettings (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    GoldGramPricePiastres INTEGER NOT NULL CHECK (GoldGramPricePiastres > 0),
    UpdatedAt TEXT NOT NULL
);
