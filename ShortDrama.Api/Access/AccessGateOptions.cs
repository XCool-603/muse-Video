using System.Security.Cryptography;
using System.Text;

namespace ShortDrama.Api.Access
{
    /// <summary>
    /// 站点访问口令门配置。
    ///
    /// 用途：给自己的私有实例加一道入口口令，避免被搜索引擎/他人误入。
    /// 这不是身份认证系统（没有用户体系），只是单一口令的准入控制。
    /// </summary>
    public class AccessGateOptions
    {
        public const string SectionName = "AccessGate";

        /// <summary>是否启用口令门</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>访问口令</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>通过后签发的 Cookie 名</summary>
        public string CookieName { get; set; } = "sd_access";

        /// <summary>通过后免密天数</summary>
        public int ValidDays { get; set; } = 30;

        /// <summary>入口页路径</summary>
        public string GatePath { get; set; } = "/gate";

        /// <summary>口令校验接口路径</summary>
        public string VerifyPath { get; set; } = "/api/v1/access";

        /// <summary>
        /// 由口令派生的 Cookie 值（不存明文口令）。
        /// 换口令即失效，无需清理已发放的 Cookie。
        /// </summary>
        public string ComputeToken()
        {
            var raw = $"shortdrama::access::{Password}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash);
        }

        /// <summary>常量时间比较，避免时序侧信道</summary>
        public bool Verify(string? candidate)
        {
            if (string.IsNullOrEmpty(candidate)) return false;
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(candidate),
                Encoding.UTF8.GetBytes(ComputeToken()));
        }
    }
}
