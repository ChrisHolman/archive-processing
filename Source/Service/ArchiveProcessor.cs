﻿using Microsoft.Extensions.Logging;
using Service.Configuration;
using Service.Messaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Service
{
    public class ArchiveProcessor : IArchiveProcessor
    {
        private readonly IAdaptationOutcomeSender _adaptationOutcomeSender;
        private readonly IAdaptationRequestSender _adaptationRequestSender;
        private readonly IFileManager _fileManager;
        private readonly IArchiveManager _archiveManager;
        private readonly IArchiveProcessorConfig _config;
        private readonly ILogger<ArchiveProcessor> _logger;

        private readonly TimeSpan _processingTimeoutDuration;
        private readonly CancellationTokenSource _cancellationTokenSource;

        private string _tmpOriginalDirectory => $"{_config.InputPath}_tmp";
        private string _tmpRebuiltDirectory => $"{_config.OutputPath}_tmp";

        public ArchiveProcessor(IAdaptationOutcomeSender adaptationOutcomeSender, IAdaptationRequestSender adaptationRequestSender, 
            IFileManager fileManager, IArchiveManager archiveManager,  IArchiveProcessorConfig config, ILogger<ArchiveProcessor> logger)
        {
            _adaptationOutcomeSender = adaptationOutcomeSender ?? throw new ArgumentNullException(nameof(adaptationOutcomeSender));
            _adaptationRequestSender = adaptationRequestSender ?? throw new ArgumentNullException(nameof(adaptationRequestSender));
            _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
            _archiveManager = archiveManager ?? throw new ArgumentNullException(nameof(archiveManager));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _processingTimeoutDuration = _config.ProcessingTimeoutDuration;
            _cancellationTokenSource = new CancellationTokenSource();

        }

        public void Process()
        {
            var task = Task.Run(() =>
            {
                return ProcessArchive();
            });

            try
            {
                bool isCompletedSuccessfully = task.Wait(_processingTimeoutDuration);

                if (!isCompletedSuccessfully)
                {
                    _logger.LogError($"File Id: {_config.ArchiveFileId} exceeded {_processingTimeoutDuration}s");
                    _cancellationTokenSource.Cancel();
                    ClearRebuiltStore(_tmpRebuiltDirectory, _config.OutputPath);
                    ClearSourceStore(_tmpOriginalDirectory);
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"File Id: {_config.ArchiveFileId} threw exception {e.Message}");
                _cancellationTokenSource.Cancel();
                ClearRebuiltStore(_tmpRebuiltDirectory, _config.OutputPath);
                ClearSourceStore(_tmpOriginalDirectory);
                _adaptationOutcomeSender.Send(FileOutcome.Error, _config.ArchiveFileId, _config.ReplyTo);
            }
        }

        private Task ProcessArchive()
        {
            _logger.LogInformation($"File Id: {_config.ArchiveFileId} processing requested");
            
            if (!_fileManager.FileExists(_config.InputPath))
            {
                throw new FileNotFoundException($"File Id: {_config.ArchiveFileId} does not exist at {_config.InputPath}");
            }

            _fileManager.CreateDirectory(_tmpOriginalDirectory);
            _fileManager.CreateDirectory(_tmpRebuiltDirectory);

            _logger.LogInformation($"File Id: {_config.ArchiveFileId} Extracting archive to temp folder {_tmpOriginalDirectory}");
            _archiveManager.ExtractArchive(_config.InputPath, _tmpOriginalDirectory);

            foreach(var file in _fileManager.GetFiles(_tmpOriginalDirectory))
            {
                var filename = Path.GetFileName(file);
                _logger.LogInformation($"File Id: {_config.ArchiveFileId}, File Name: {filename} about to be sent");

                var status = _adaptationRequestSender.Send(Guid.NewGuid().ToString(), file, $"{_tmpRebuiltDirectory}/{filename}", _cancellationTokenSource.Token);

                _logger.LogInformation($"File Id: {_config.ArchiveFileId}, File Name: {filename}, status: {status}");
            }

            _logger.LogInformation($"File Id: {_config.ArchiveFileId} Creating archive.");
            _archiveManager.CreateArchive(_tmpRebuiltDirectory, _config.OutputPath);

            _adaptationOutcomeSender.Send(FileOutcome.Replace, _config.ArchiveFileId, _config.ReplyTo);
            ClearSourceStore(_tmpOriginalDirectory);
            ClearRebuiltStore(_tmpRebuiltDirectory);

            return Task.CompletedTask;
        }

        private void ClearRebuiltStore(string tempDirectory)
        {
            if (_fileManager.DirectoryExists(tempDirectory))
            {
                _fileManager.DeleteDirectory(tempDirectory);
            }
        }

        private void ClearRebuiltStore(string tempDirectory, string filePath)
        {
            if (_fileManager.DirectoryExists(tempDirectory))
            {
                _fileManager.DeleteDirectory(tempDirectory);
            }

            if (_fileManager.FileExists(filePath))
            {
                _fileManager.DeleteFile(filePath);
            }
        }

        private void ClearSourceStore(string tempDirectory)
        {
            if (_fileManager.DirectoryExists(tempDirectory))
            {
                _fileManager.DeleteDirectory(tempDirectory);
            }
        }
    }
}
