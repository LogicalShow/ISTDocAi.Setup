using System.Text;
using System.Text.RegularExpressions;

namespace ISTDocAi.OperatorTool;

public static class OperatorGrants
{
    public static string Generate(string database, string operators, string modelTeam = "", string quotaOperators = "")
    {
        if (!Regex.IsMatch(database, @"\A[A-Za-z][A-Za-z0-9_]{0,127}\z")) throw new ArgumentException("Invalid database name.");
        if (string.IsNullOrWhiteSpace(operators)) throw new ArgumentException("Operator group required.");
        var sql = new StringBuilder("-- Run AFTER DocAi has created and migrated the database.\r\n");
        foreach (var (name, role) in new[] { (operators, "docai_operator"), (modelTeam, "docai_modelteam"), (quotaOperators, "docai_quota_operator") })
        {
            if (name.Length == 0) continue;
            if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, @"\A[A-Za-z0-9_. $\\-]{1,128}\z")) throw new ArgumentException("Invalid Windows login name.");
            sql.Append($"USE [master];\r\nGO\r\nIF SUSER_ID(N'{name}') IS NULL CREATE LOGIN [{name}] FROM WINDOWS;\r\nGO\r\nUSE [{database}];\r\nGO\r\nIF DATABASE_PRINCIPAL_ID(N'{name}') IS NULL CREATE USER [{name}] FOR LOGIN [{name}];\r\nGO\r\nIF NOT EXISTS (SELECT 1 FROM sys.database_role_members WHERE role_principal_id=DATABASE_PRINCIPAL_ID(N'{role}') AND member_principal_id=DATABASE_PRINCIPAL_ID(N'{name}')) ALTER ROLE [{role}] ADD MEMBER [{name}];\r\nGO\r\n");
        }
        return sql.ToString();
    }
}
