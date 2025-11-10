using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoMoq;
using Emby.Server.Implementations.IO;
using Emby.Server.Implementations.Library;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Library
{
    public class MediaSourceManagerTests
    {
        private readonly MediaSourceManager _mediaSourceManager;

        public MediaSourceManagerTests()
        {
            IFixture fixture = new Fixture().Customize(new AutoMoqCustomization { ConfigureMembers = true });
            fixture.Inject<IFileSystem>(fixture.Create<ManagedFileSystem>());
            _mediaSourceManager = fixture.Create<MediaSourceManager>();
        }

        [Theory]
        [InlineData(@"C:\mydir\myfile.ext", MediaProtocol.File)]
        [InlineData("/mydir/myfile.ext", MediaProtocol.File)]
        [InlineData("file:///mydir/myfile.ext", MediaProtocol.File)]
        [InlineData("http://example.com/stream.m3u8", MediaProtocol.Http)]
        [InlineData("https://example.com/stream.m3u8", MediaProtocol.Http)]
        [InlineData("rtsp://media.example.com:554/twister/audiotrack", MediaProtocol.Rtsp)]
        public void GetPathProtocol_ValidArg_Correct(string path, MediaProtocol expected)
            => Assert.Equal(expected, _mediaSourceManager.GetPathProtocol(path));

        [Fact]
        public async Task GetRecordingStreamMediaSources_ShouldNotUseInternalIpAddress()
        {
            var fixture = new Fixture().Customize(new AutoMoqCustomization { ConfigureMembers = true });

            var mockAppHost = fixture.Freeze<Mock<IServerApplicationHost>>();

            // Mock GetApiUrlForLocalAccess to return internal IP (the bug behavior)
            mockAppHost
                .Setup(x => x.GetApiUrlForLocalAccess(It.IsAny<IPAddress>(), It.IsAny<bool>()))
                .Returns("http://172.19.0.3:8096");

            // Mock GetSmartApiUrl to return public URL (the correct behavior)
            mockAppHost
                .Setup(x => x.GetSmartApiUrl(It.IsAny<IPAddress>()))
                .Returns("https://mydomain.com");

            fixture.Inject<IFileSystem>(fixture.Create<ManagedFileSystem>());
            var mediaSourceManager = fixture.Create<MediaSourceManager>();

            var recordingInfo = new ActiveRecordingInfo
            {
                Id = "test-recording-123",
                Path = "/cache/recording.ts"
            };

            var mediaSources = await mediaSourceManager.GetRecordingStreamMediaSources(
                recordingInfo,
                CancellationToken.None);

            var mediaSource = mediaSources.FirstOrDefault();
            Assert.NotNull(mediaSource);
            Assert.NotNull(mediaSource.EncoderPath);

            // The code should use GetSmartApiUrl which returns the public URL,
            // NOT GetApiUrlForLocalAccess which returns the internal IP
            Assert.DoesNotContain("172.19.", mediaSource.EncoderPath, StringComparison.Ordinal);
            Assert.DoesNotContain("127.0.0.1", mediaSource.EncoderPath, StringComparison.Ordinal);
            Assert.DoesNotContain("localhost", mediaSource.EncoderPath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("https://mydomain.com", mediaSource.EncoderPath, StringComparison.Ordinal);
        }
    }
}
