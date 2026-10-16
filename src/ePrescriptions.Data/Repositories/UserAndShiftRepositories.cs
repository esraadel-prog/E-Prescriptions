using System;
using ePrescriptions.Core.Helpers;
using System.Collections.Generic;
using System.Data.SQLite;
using ePrescriptions.Core.Security;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Data.Repositories
{
    public class UserRepository
    {
        private readonly DatabaseContext _dbContext;

        public UserRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>عدد المحاولات الفاشلة المتتالية قبل القفل المؤقت</summary>
        public const int MaxFailedAttempts = 5;
        /// <summary>مدة القفل المؤقت بالدقائق</summary>
        public const int LockoutMinutes = 5;

        /// <summary>مصدر الوقت (قابل للاستبدال في الاختبارات)</summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

        public User? Authenticate(string username, string password) => Authenticate(username, password, out _);

        /// <summary>
        /// تسجيل الدخول: بعد MaxFailedAttempts محاولات فاشلة متتالية الحساب بيتقفل LockoutMinutes (حتى بكلمة المرور الصحيحة)،
        /// والدخول الناجح بيصفّر العدّاد وبيحدّث التجزئة تلقائياً لو بصيغة قديمة (SHA1) أو تكرارات أقل.
        /// errorMessage بيتملى فقط لما الحساب مقفول مؤقتاً.
        /// </summary>
        private static string? GetStringOrNull(SQLiteDataReader reader, string col)
        {
            try
            {
                int ordinal = reader.GetOrdinal(col);
                return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
            }
            catch
            {
                return null;
            }
        }

        private class AuthCandidate
        {
            public int Id;
            public string Username = string.Empty;
            public string FullName = string.Empty;
            public string StoredHash = string.Empty;
            public int Role;
            public string CreatedAt = string.Empty;
            public int Failed;
            public string? LockedUntilText;
            public int MatchPriority;
            public string? CustomPermissionsConfig;
        }

        public User? Authenticate(string username, string password, out string errorMessage)
        {
            errorMessage = string.Empty;
            string trimmed = (username ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;

            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Users WHERE IsActive = 1;";


            string normInput = ArabicTextNormalizer.Normalize(trimmed);
            string noAlInput = normInput.StartsWith("ال") && normInput.Length > 2 ? normInput.Substring(2) : normInput;
            bool isInputAdmin = normInput.Equals("admin", StringComparison.OrdinalIgnoreCase)
                             || normInput == "ادمن"
                             || noAlInput == "ادمن"
                             || normInput == "مدير"
                             || noAlInput == "مدير"
                             || normInput.Equals("administrator", StringComparison.OrdinalIgnoreCase);

            var candidates = new List<AuthCandidate>();

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string dbUname = reader["Username"].ToString() ?? string.Empty;
                    string dbFullName = reader["FullName"].ToString() ?? string.Empty;
                    int dbRole = Convert.ToInt32(reader["Role"]);

                    int priority = 0;
                    if (string.Equals(dbUname, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        priority = 1;
                    }
                    else if (string.Equals(dbFullName, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        priority = 2;
                    }
                    else
                    {
                        string normDbUname = ArabicTextNormalizer.Normalize(dbUname);
                        string normDbFullName = ArabicTextNormalizer.Normalize(dbFullName);
                        string noAlDbUname = normDbUname.StartsWith("ال") && normDbUname.Length > 2 ? normDbUname.Substring(2) : normDbUname;
                        string noAlDbFullName = normDbFullName.StartsWith("ال") && normDbFullName.Length > 2 ? normDbFullName.Substring(2) : normDbFullName;

                        if (normDbUname == normInput) priority = 3;
                        else if (normDbFullName == normInput) priority = 4;
                        else if (noAlDbUname == noAlInput || noAlDbFullName == noAlInput) priority = 5;
                        else if (isInputAdmin && (normDbUname.Equals("admin", StringComparison.OrdinalIgnoreCase) || normDbUname == "ادمن" || noAlDbUname == "ادمن" ||
                                                 normDbFullName.Equals("admin", StringComparison.OrdinalIgnoreCase) || normDbFullName == "ادمن" || noAlDbFullName == "ادمن" ||
                                                 normDbUname == "مدير" || normDbFullName == "مدير" || dbRole == (int)UserRole.Admin || dbRole == 99))
                        {
                            priority = 6;
                        }
                    }

                    if (priority > 0)
                    {
                        candidates.Add(new AuthCandidate
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Username = dbUname,
                            FullName = dbFullName,
                            StoredHash = reader["PasswordHash"].ToString() ?? string.Empty,
                            Role = dbRole,
                            CreatedAt = reader["CreatedAt"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
                            Failed = Convert.ToInt32(reader["FailedLoginCount"]),
                            LockedUntilText = reader["LockedUntil"] is DBNull ? null : reader["LockedUntil"].ToString(),
                            MatchPriority = priority,
                            CustomPermissionsConfig = GetStringOrNull(reader, "CustomPermissionsConfig")
                        });
                    }
                }
            }

            if (candidates.Count == 0) return null;

            candidates.Sort((a, b) => a.MatchPriority.CompareTo(b.MatchPriority));

            AuthCandidate? matched = null;
            foreach (var candidate in candidates)
            {
                if (PasswordHasher.VerifyPassword(password ?? string.Empty, candidate.StoredHash))
                {
                    matched = candidate;
                    break;
                }
            }

            DateTime now = Clock();

            if (matched != null)
            {
                if (matched.LockedUntilText != null && DbDate.TryParse(matched.LockedUntilText, out DateTime lockedUntil) && lockedUntil > now)
                {
                    int minutes = (int)Math.Ceiling((lockedUntil - now).TotalMinutes);
                    errorMessage = $"الحساب مقفول مؤقتاً بسبب محاولات دخول فاشلة. حاول بعد {Math.Max(1, minutes)} دقيقة.";
                    return null;
                }

                using var okCmd = conn.CreateCommand();
                if (PasswordHasher.NeedsRehash(matched.StoredHash))
                {
                    string newHash = PasswordHasher.HashPassword(password!);
                    okCmd.CommandText = "UPDATE Users SET FailedLoginCount = 0, LockedUntil = NULL, PasswordHash = @h WHERE Id = @id;";
                    okCmd.Parameters.AddWithValue("@h", newHash);
                    matched.StoredHash = newHash;
                }
                else
                {
                    okCmd.CommandText = "UPDATE Users SET FailedLoginCount = 0, LockedUntil = NULL WHERE Id = @id;";
                }
                okCmd.Parameters.AddWithValue("@id", matched.Id);
                okCmd.ExecuteNonQuery();

                return new User
                {
                    Id = matched.Id,
                    Username = matched.Username,
                    FullName = matched.FullName,
                    PasswordHash = matched.StoredHash,
                    Role = (UserRole)matched.Role,
                    IsActive = true,
                    CreatedAt = DbDate.Parse(matched.CreatedAt),
                    CustomPermissionsConfig = matched.CustomPermissionsConfig
                };
            }


            var primaryTarget = candidates[0];
            primaryTarget.Failed++;
            using (var failCmd = conn.CreateCommand())
            {
                if (primaryTarget.Failed >= MaxFailedAttempts)
                {
                    failCmd.CommandText = "UPDATE Users SET FailedLoginCount = 0, LockedUntil = @lu WHERE Id = @id;";
                    failCmd.Parameters.AddWithValue("@lu", now.AddMinutes(LockoutMinutes).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                    errorMessage = $"تم قفل الحساب {LockoutMinutes} دقائق بسبب تكرار المحاولات الفاشلة.";
                }
                else
                {
                    failCmd.CommandText = "UPDATE Users SET FailedLoginCount = @c WHERE Id = @id;";
                    failCmd.Parameters.AddWithValue("@c", primaryTarget.Failed);
                }
                failCmd.Parameters.AddWithValue("@id", primaryTarget.Id);
                failCmd.ExecuteNonQuery();
            }

            return null;
        }

        /// <summary>
        /// كل المستخدمين عدا صاحب الصيدلية (Role = Owner)، فلا يظهر في أي شاشة إدارة موظفين أو صلاحيات.
        /// </summary>
        public List<User> GetAll(bool includeOwner = false)
        {
            var list = new List<User>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = includeOwner
                ? "SELECT * FROM Users ORDER BY FullName ASC;"
                : "SELECT * FROM Users WHERE Role <> 99 ORDER BY FullName ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new User
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Username = reader["Username"].ToString() ?? string.Empty,
                    FullName = reader["FullName"].ToString() ?? string.Empty,
                    Role = (UserRole)Convert.ToInt32(reader["Role"]),
                    IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
                    CreatedAt = DbDate.Parse(reader["CreatedAt"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture)),
                    CustomPermissionsConfig = GetStringOrNull(reader, "CustomPermissionsConfig")
                });
            }
            return list;
        }

        public int SaveStaffUser(int id, string username, string fullName, string? plainPassword, UserRole role, bool isActive) =>
            SaveStaffUser(id, username, fullName, plainPassword, role, isActive, null);

        /// <summary>
        /// إنشاء أو تعديل حساب موظف (مدير إدارة / صيدلي / كاشير) مع خيار حفظ الصلاحيات المخصصة.
        /// </summary>
        public int SaveStaffUser(int id, string username, string fullName, string? plainPassword, UserRole role, bool isActive, string? customPermissionsConfig)
        {
            if (role == UserRole.Owner)
                throw new InvalidOperationException("لا يمكن إنشاء أو تعديل حساب صاحب الصيدلية من شاشة إدارة الموظفين.");

            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            if (id == 0)
            {
                string normPass = ArabicTextNormalizer.NormalizeDigits(plainPassword ?? throw new ArgumentException("كلمة المرور مطلوبة للمستخدم الجديد."));
                string hash = PasswordHasher.HashPassword(normPass);
                cmd.CommandText = @"
                    INSERT INTO Users (Username, FullName, PasswordHash, Role, IsActive, CreatedAt, CustomPermissionsConfig)
                    VALUES (@u, @f, @h, @r, @a, @c, @perm);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@h", hash);
                cmd.Parameters.AddWithValue("@c", DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@perm", (object?)customPermissionsConfig ?? DBNull.Value);
            }
            else
            {
                // التأكد من أن الحساب المستهدف ليس حساب صاحب الصيدلية قبل التعديل
                using (var guardCmd = conn.CreateCommand())
                {
                    guardCmd.CommandText = "SELECT Role FROM Users WHERE Id = @id;";
                    guardCmd.Parameters.AddWithValue("@id", id);
                    object? existingRole = guardCmd.ExecuteScalar();
                    if (existingRole != null && Convert.ToInt32(existingRole) == 99)
                        throw new InvalidOperationException("لا يمكن تعديل حساب صاحب الصيدلية من شاشة إدارة الموظفين.");
                }

                if (!string.IsNullOrWhiteSpace(plainPassword))
                {
                    string normPass = ArabicTextNormalizer.NormalizeDigits(plainPassword);
                    string hash = PasswordHasher.HashPassword(normPass);
                    cmd.CommandText = customPermissionsConfig != null
                        ? @"UPDATE Users SET Username=@u, FullName=@f, PasswordHash=@h, Role=@r, IsActive=@a, CustomPermissionsConfig=@perm, FailedLoginCount=0, LockedUntil=NULL
                            WHERE Id=@id AND Role <> 99;"
                        : @"UPDATE Users SET Username=@u, FullName=@f, PasswordHash=@h, Role=@r, IsActive=@a, FailedLoginCount=0, LockedUntil=NULL
                            WHERE Id=@id AND Role <> 99;";
                    cmd.Parameters.AddWithValue("@h", hash);
                }
                else
                {
                    cmd.CommandText = customPermissionsConfig != null
                        ? @"UPDATE Users SET Username=@u, FullName=@f, Role=@r, IsActive=@a, CustomPermissionsConfig=@perm
                            WHERE Id=@id AND Role <> 99;"
                        : @"UPDATE Users SET Username=@u, FullName=@f, Role=@r, IsActive=@a
                            WHERE Id=@id AND Role <> 99;";
                }
                if (customPermissionsConfig != null)
                {
                    cmd.Parameters.AddWithValue("@perm", customPermissionsConfig);
                }
                cmd.Parameters.AddWithValue("@id", id);
            }

            cmd.Parameters.AddWithValue("@u", username.Trim());
            cmd.Parameters.AddWithValue("@f", fullName.Trim());
            cmd.Parameters.AddWithValue("@r", (int)role);
            cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);

            if (id == 0) return Convert.ToInt32(cmd.ExecuteScalar());

            cmd.ExecuteNonQuery();
            return id;
        }

        /// <summary>
        /// حفظ أو إلغاء الصلاحيات المخصصة لموظف محدد
        /// </summary>
        public void SaveUserPermissions(int userId, string? customPermissionsConfig)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET CustomPermissionsConfig = @perm WHERE Id = @id AND Role <> 99;";
            cmd.Parameters.AddWithValue("@perm", (object?)customPermissionsConfig ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", userId);
            cmd.ExecuteNonQuery();
        }


        /// <summary>
        /// يغيّر مستخدم كلمة مرور حسابه هو فقط (بعد التحقق من كلمة المرور الحالية).
        /// تُستخدم هذه الدالة لتغيير كلمة مرور صاحب الصيدلية الافتراضية بعد أول دخول.
        /// </summary>
        public bool ChangeOwnPassword(int userId, string currentPassword, string newPassword, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            {
                errorMessage = "كلمة المرور الجديدة يجب ألا تقل عن 8 أحرف.";
                return false;
            }

            // خطوة 46: ممنوع تغيير كلمة المرور لكلمة افتراضية معروفة أو لنفس الحالية
            if (ePrescriptions.Core.Security.DefaultCredentials.IsKnownDefaultPassword(newPassword))
            {
                errorMessage = "لا يمكن استخدام كلمة المرور الافتراضية كلمةً جديدة.";
                return false;
            }
            if (string.Equals(newPassword, currentPassword, StringComparison.Ordinal))
            {
                errorMessage = "كلمة المرور الجديدة يجب أن تختلف عن الحالية.";
                return false;
            }

            using var conn = _dbContext.CreateConnection();
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT PasswordHash FROM Users WHERE Id = @id;";
            checkCmd.Parameters.AddWithValue("@id", userId);
            object? storedHashObj = checkCmd.ExecuteScalar();

            if (storedHashObj == null)
            {
                errorMessage = "تعذر العثور على الحساب.";
                return false;
            }

            string storedHash = storedHashObj.ToString() ?? string.Empty;
            if (!PasswordHasher.VerifyPassword(currentPassword, storedHash))
            {
                errorMessage = "كلمة المرور الحالية غير صحيحة.";
                return false;
            }

            string newHash = PasswordHasher.HashPassword(ArabicTextNormalizer.NormalizeDigits(newPassword));
            using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = "UPDATE Users SET PasswordHash = @h WHERE Id = @id;";
            updateCmd.Parameters.AddWithValue("@h", newHash);
            updateCmd.Parameters.AddWithValue("@id", userId);
            updateCmd.ExecuteNonQuery();

            return true;
        }
    }

    public class ShiftRepository
    {
        private readonly DatabaseContext _dbContext;

        public ShiftRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Shift? GetCurrentOpenShift(int userId)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, UserId, UserName, StartTime, EndTime, OpeningBalancePiastres,
                       TotalSalesCashPiastres, TotalSalesNetworkPiastres, TotalSalesOnAccountPiastres,
                       TotalReturnsCashPiastres, ExpectedCashPiastres, ActualCashPiastres, DifferencePiastres,
                       Status, Notes
                FROM Shifts
                WHERE UserId = @uId AND Status = 1
                ORDER BY Id DESC
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@uId", userId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return MapShift(reader);
            }

            return null;
        }

        public int OpenShift(int userId, string userName, long openingBalancePiastres)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Shifts (UserId, UserName, StartTime, OpeningBalancePiastres, ExpectedCashPiastres, Status)
                VALUES (@uId, @uName, @start, @openBal, @openBal, 1);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@uId", userId);
            cmd.Parameters.AddWithValue("@uName", userName);
            cmd.Parameters.AddWithValue("@start", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@openBal", openingBalancePiastres);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void CloseShift(int shiftId, long actualCashPiastres, string? notes)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            // حساب الفارق بين الفعلي والمتوقع: Difference = Actual - Expected
            cmd.CommandText = @"
                UPDATE Shifts
                SET EndTime = @end,
                    ActualCashPiastres = @actual,
                    DifferencePiastres = @actual - ExpectedCashPiastres,
                    Status = 2,
                    Notes = @notes
                WHERE Id = @sId;";

            cmd.Parameters.AddWithValue("@end", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@actual", actualCashPiastres);
            cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sId", shiftId);

            cmd.ExecuteNonQuery();
        }

        public Shift? GetShiftById(int shiftId)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, UserId, UserName, StartTime, EndTime, OpeningBalancePiastres,
                       TotalSalesCashPiastres, TotalSalesNetworkPiastres, TotalSalesOnAccountPiastres,
                       TotalReturnsCashPiastres, ExpectedCashPiastres, ActualCashPiastres, DifferencePiastres,
                       Status, Notes
                FROM Shifts
                WHERE Id = @sId
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@sId", shiftId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return MapShift(reader);
            }
            return null;
        }

        public List<Shift> GetShifts(DateTime? fromDate = null, DateTime? toDate = null, int? userId = null, ShiftStatus? status = null)
        {
            var list = new List<Shift>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            var conditions = new List<string>();
            if (fromDate.HasValue)
            {
                conditions.Add("date(StartTime) >= date(@from)");
                cmd.Parameters.AddWithValue("@from", fromDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (toDate.HasValue)
            {
                conditions.Add("date(StartTime) <= date(@to)");
                cmd.Parameters.AddWithValue("@to", toDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (userId.HasValue && userId.Value > 0)
            {
                conditions.Add("UserId = @uId");
                cmd.Parameters.AddWithValue("@uId", userId.Value);
            }
            if (status.HasValue)
            {
                conditions.Add("Status = @status");
                cmd.Parameters.AddWithValue("@status", (int)status.Value);
            }

            string whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

            cmd.CommandText = $@"
                SELECT Id, UserId, UserName, StartTime, EndTime, OpeningBalancePiastres,
                       TotalSalesCashPiastres, TotalSalesNetworkPiastres, TotalSalesOnAccountPiastres,
                       TotalReturnsCashPiastres, ExpectedCashPiastres, ActualCashPiastres, DifferencePiastres,
                       Status, Notes
                FROM Shifts
                {whereClause}
                ORDER BY Id DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapShift(reader));
            }
            return list;
        }

        private static Shift MapShift(SQLiteDataReader reader)
        {
            return new Shift
            {
                Id = Convert.ToInt32(reader["Id"]),
                UserId = Convert.ToInt32(reader["UserId"]),
                UserName = reader["UserName"].ToString() ?? string.Empty,
                StartTime = DbDate.Parse(reader["StartTime"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture)),
                EndTime = reader["EndTime"] is DBNull ? null : DbDate.Parse(reader["EndTime"].ToString()!),
                OpeningBalancePiastres = Convert.ToInt64(reader["OpeningBalancePiastres"]),
                TotalSalesCashPiastres = Convert.ToInt64(reader["TotalSalesCashPiastres"]),
                TotalSalesNetworkPiastres = Convert.ToInt64(reader["TotalSalesNetworkPiastres"]),
                TotalSalesOnAccountPiastres = Convert.ToInt64(reader["TotalSalesOnAccountPiastres"]),
                TotalReturnsCashPiastres = Convert.ToInt64(reader["TotalReturnsCashPiastres"]),
                ExpectedCashPiastres = Convert.ToInt64(reader["ExpectedCashPiastres"]),
                ActualCashPiastres = reader["ActualCashPiastres"] is DBNull ? null : Convert.ToInt64(reader["ActualCashPiastres"]),
                DifferencePiastres = reader["DifferencePiastres"] is DBNull ? null : Convert.ToInt64(reader["DifferencePiastres"]),
                Status = (ShiftStatus)Convert.ToInt32(reader["Status"]),
                Notes = reader["Notes"] is DBNull ? null : reader["Notes"].ToString()
            };
        }
    }
}
