using ShortDrama.Application.Common;
using Xunit;

namespace ShortDrama.Tests
{
    /// <summary>跨平台同剧去重的标题相似度算法测试。</summary>
    public class TextSimilarityTests
    {
        [Theory]
        [InlineData("巅峰狂少：我有神豪系统", "巅峰狂少:我有神豪系统")]
        [InlineData("逆袭人生", "逆袭人生 ")]
        [InlineData("重生之我在八零当首富", "重生之我在八零当首富（短剧）")]
        public void Similarity_SameDramaWithNoise_ShouldMatch(string a, string b)
        {
            Assert.True(TextSimilarity.Similarity(a, b) >= 0.85,
                $"\"{a}\" 与 \"{b}\" 应判定为同一部剧");
        }

        [Fact]
        public void Similarity_ContainmentCase_ShouldMatch()
        {
            // 平台A叫《逆袭人生》，平台B叫《逆袭人生从摆摊开始》——属于同剧不同命名
            var score = TextSimilarity.Similarity("逆袭人生", "逆袭人生从摆摊开始");
            Assert.True(score >= 0.85, $"包含关系应视为同剧，实际得分 {score}");
        }

        [Theory]
        [InlineData("巅峰狂少", "隐世医仙在都市")]
        [InlineData("重生之我在八零当首富", "闪婚老公竟是隐藏首富")]
        [InlineData("种田小福女", "镇国战神归来")]
        public void Similarity_DifferentDramas_ShouldNotMatch(string a, string b)
        {
            Assert.True(TextSimilarity.Similarity(a, b) < 0.85,
                $"\"{a}\" 与 \"{b}\" 不应被合并");
        }

        [Fact]
        public void Normalize_StripsPlatformSuffix()
        {
            Assert.Equal("巅峰狂少", TextSimilarity.Normalize("巅峰狂少短剧"));
            Assert.Equal("巅峰狂少", TextSimilarity.Normalize("巅峰狂少·全集"));
        }

        [Fact]
        public void Similarity_EmptyInputs_AreHandled()
        {
            Assert.Equal(1, TextSimilarity.Similarity("", ""));
            Assert.Equal(0, TextSimilarity.Similarity("abc", ""));
        }
    }
}
