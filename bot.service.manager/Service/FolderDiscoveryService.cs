﻿using bot.service.manager.IService;
using bot.service.manager.Model;

namespace bot.service.manager.Service
{
    public class FolderDiscoveryService : IFolderDiscoveryService
    {
        private readonly CommonService _commonService;

        public FolderDiscoveryService(CommonService commonService)
        {
            _commonService = commonService;
        }

        public async Task<FolderDiscovery> GetFolderDetailService(string targetDirectory)
        {
            if (string.IsNullOrEmpty(targetDirectory))
                targetDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "k8-workspace"));

            if (!Directory.Exists(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            var result = await GetAllDirectory(targetDirectory);
            result.RootDirectory = Directory.GetCurrentDirectory();
            return result;
        }

        public async Task<List<FileDetail>> GetAllFileService(string targetDirectory)
        {
            if (string.IsNullOrEmpty(targetDirectory))
                throw new Exception("Directory is invalid");

            if (!Directory.Exists(targetDirectory))
                throw new Exception("Directory not found");

            var result = await GetAllFilesInDirectory(targetDirectory);
            return result;
        }

        private async Task<List<FileDetail>> GetAllFilesInDirectory(string targetDirectory)
        {
            var files = new List<FileDetail>();
            string[] fileEntries = Directory.GetFiles(targetDirectory);

            foreach (string filePath in fileEntries)
            {
                string extension = Path.GetExtension(filePath);
                if (extension.Equals(".yml") || extension.Equals(".yaml"))
                {
                    string fileName = "";
                    if (filePath.Contains(@"\"))
                        fileName = filePath.Split(@"\").Last();
                    else
                        fileName = filePath.Split(@"/").Last();

                    files.Add(new FileDetail
                    {
                        FullPath = filePath,
                        FileName = fileName
                    });
                }
            }

            return await Task.FromResult(files);
        }

        private async Task<FolderDiscovery> GetAllDirectory(string targetDirectory)
        {
            FolderDiscovery folderDiscovery = new FolderDiscovery();
            string[] subdirectoryEntries = Directory.GetDirectories(targetDirectory);
            folderDiscovery.FolderPath = targetDirectory;
            if (folderDiscovery.FolderPath.Contains(@"\"))
                folderDiscovery.FolderName = targetDirectory.Split(@"\").Last();
            else
                folderDiscovery.FolderName = targetDirectory.Split(@"/").Last();

            if (subdirectoryEntries != null && subdirectoryEntries.Length > 0)
            {
                folderDiscovery.Folders = new List<FolderDetail>();
                foreach (var folder in subdirectoryEntries)
                {
                    string folderName = "";
                    if (folder.Contains(@"\"))
                        folderName = folder.Split(@"\").Last();
                    else
                        folderName = folder.Split(@"/").Last();

                    folderDiscovery.Folders.Add(new FolderDetail
                    {
                        FullPath = folder,
                        FolderName = folderName
                    });
                }
            }
            return await Task.FromResult(folderDiscovery);
        }

        public async Task<string> RunCommandService(KubectlModel kubectlModel)
        {
            var result = await _commonService.RunAllCommandService(kubectlModel);
            return result;
        }
    }
}
