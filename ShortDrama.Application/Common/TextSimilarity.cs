using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ShortDrama.Application.Common
{
    /// <summary>标题相似度工具，用于跨平台同剧去重合并。</summary>
    public static class TextSimilarity
    {
        private static readonly char[] NoiseChars =
        {
            ' ', '\t', '：', ':', '，', ',', '。', '.', '！', '!', '？', '?', '、',
            '「', '」', '《', '》', '（', '）', '(', ')', '-', '_', '·', '～', '~', '"', '\''
        };

        /// <summary>归一化标题：去噪、去平台后缀、统一大小写</summary>
        public static string Normalize(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var sb = new StringBuilder(title.Length);
            foreach (var ch in title)
            {
                if (NoiseChars.Contains(ch)) continue;
                sb.Append(char.ToLowerInvariant(ch));
            }

            var result = sb.ToString();

            // 去掉常见平台后缀
            foreach (var suffix in new[] { "短剧", "全集", "完整版", "高清" })
            {
                if (result.EndsWith(suffix, StringComparison.Ordinal))
                {
                    result = result[..^suffix.Length];
                }
            }

            return result;
        }

        /// <summary>计算 0~1 的相似度（基于编辑距离）</summary>
        public static double Similarity(string? a, string? b)
        {
            var x = Normalize(a);
            var y = Normalize(b);

            if (x.Length == 0 && y.Length == 0) return 1;
            if (x.Length == 0 || y.Length == 0) return 0;
            if (x == y) return 1;

            // 一方是另一方的前缀（平台常对标题做截断/扩写，如「逆袭人生」vs「逆袭人生从摆摊开始」）
            // 为避免误合并，要求较短标题至少 4 个字符且覆盖率不低于 40%
            var shorterIsPrefix = x.Length <= y.Length
                ? y.StartsWith(x, StringComparison.Ordinal)
                : x.StartsWith(y, StringComparison.Ordinal);

            if (shorterIsPrefix)
            {
                var shorter = Math.Min(x.Length, y.Length);
                var longer = Math.Max(x.Length, y.Length);
                var containment = (double)shorter / longer;

                if (shorter >= 4 && containment >= 0.4)
                {
                    return Math.Max(containment, 0.85);
                }
            }

            var distance = Levenshtein(x, y);
            var maxLen = Math.Max(x.Length, y.Length);
            return 1.0 - (double)distance / maxLen;
        }

        private static int Levenshtein(string s, string t)
        {
            var n = s.Length;
            var m = t.Length;
            var d = new int[n + 1, m + 1];

            for (var i = 0; i <= n; i++) d[i, 0] = i;
            for (var j = 0; j <= m; j++) d[0, j] = j;

            for (var i = 1; i <= n; i++)
            {
                for (var j = 1; j <= m; j++)
                {
                    var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }
    }
}
