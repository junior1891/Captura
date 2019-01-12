﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace Captura
{
    public class YouTubeUploadRequest : IDisposable
    {
        readonly VideosResource.InsertMediaUpload _videoInsertRequest;
        readonly Stream _dataStream;

        internal YouTubeUploadRequest(string FileName,
            YouTubeService YouTubeService,
            Video Video)
        {
            _dataStream = new FileStream(FileName, FileMode.Open);
            _videoInsertRequest = YouTubeService.Videos.Insert(Video, "snippet,status", _dataStream, "video/*");

            _videoInsertRequest.ProgressChanged += VideosInsertRequest_ProgressChanged;
            _videoInsertRequest.ResponseReceived += VideosInsertRequest_ResponseReceived;
        }

        void VideosInsertRequest_ProgressChanged(IUploadProgress Progress)
        {
            BytesSent?.Invoke(Progress.BytesSent);
        }

        void VideosInsertRequest_ResponseReceived(Video Video)
        {
            Uploaded?.Invoke($"https://youtube.com/watch?v={Video.Id}");
        }

        public async Task<IUploadProgress> Upload(CancellationToken CancellationToken)
        {
            return await _videoInsertRequest.UploadAsync(CancellationToken);
        }

        public event Action<long> BytesSent;

        public event Action<string> Uploaded;

        public void Dispose()
        {
            _dataStream.Dispose();
        }
    }
}