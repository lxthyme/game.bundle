using System.Text;
using Xunit;
using DspBlueprintTransform.Blueprint;

namespace DspBlueprintTransform.Blueprint.Tests
{
    public class BlueprintChecksumTests
    {
        [Theory]
        [InlineData("", "84D1CE3BD68F49AB26EB0F96416617CF")]
        [InlineData("a", "F10BDDAECB62E5A92433757867EE06DB")]
        [InlineData("abc", "F8D437E8A2D3C2138BC18EF62D8CFC64")]
        [InlineData("BLUEPRINT:1,10,0,0,0,0,0,0,0,123456,0.10.34.28281,test", "AB8CAA6FF98424011783D6C7FCF5E28C")]
        [InlineData("The quick brown fox jumps over the lazy dog", "86DCC27D895972046BC51C8EACA17F64")]
        public void HexDigest_MatchesReferenceVectors(string input, string expectedHex)
        {
            string actual = BlueprintChecksum.HexDigest(Encoding.ASCII.GetBytes(input));
            Assert.Equal(expectedHex, actual);
        }
    }
}
