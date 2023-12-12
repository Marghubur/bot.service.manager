﻿using bot.service.manager.IService;
using bot.service.manager.Model;
using Microsoft.Extensions.Options;
using Octokit;
using static bot.service.manager.Service.PodHelper;

namespace bot.service.manager.Service
{
    public class FolderDiscoveryService : IFolderDiscoveryService
    {
        private readonly CommonService _commonService;
        private readonly PodHelper _podHelper;
        private readonly YamlUtilService _yamlUtilService;
        private readonly ILogger<FolderDiscoveryService> _logger;
        private readonly RemoteServerConfig _remoteServerConfig;

        public FolderDiscoveryService(CommonService commonService,
            PodHelper podHelper,
            YamlUtilService yamlUtilService,
            ILogger<FolderDiscoveryService> logger,
            IOptions<RemoteServerConfig> options)
        {
            _commonService = commonService;
            _podHelper = podHelper;
            _yamlUtilService = yamlUtilService;
            _logger = logger;
            _remoteServerConfig = options.Value;
        }

        public async Task<List<GitHubContent>> GetLinuxFolderDetail(string targetDirectory)
        {
            string owner = "Istiyakmi9";
            string repo = "ems-k8s";
            string accessToken = "";

            List<GitHubContent> gitHubContent = new List<GitHubContent>();

            GitHubClient client = new GitHubClient(new ProductHeaderValue("GitHubApiExample"));
            var tokenAuth = new Credentials(accessToken);
            client.Credentials = tokenAuth;

            try
            {
                IReadOnlyList<RepositoryContent> contents = await client.Repository.Content.GetAllContents(owner, repo, targetDirectory);

                foreach (var content in contents)
                {
                    if ((content.Type == ContentType.File && (Path.GetExtension(content.Path).Equals(".yaml") || Path.GetExtension(content.Path).Equals(".yml"))) || content.Type == ContentType.Dir)
                    {
                        gitHubContent.Add(new GitHubContent
                        {
                            Type = content.Type.StringValue,
                            Name = content.Name,
                            DownloadUrl = content.DownloadUrl,
                            GitUrl = content.GitUrl,
                            Url = content.Url,
                            Path = content.Path,
                            Sha = content.Sha
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Repository not found.");
                throw new Exception(ex.Message);
            }

            return gitHubContent;
        }

        private async Task ProcessDirectory(GitHubClient client, string owner, string repoName, string path)
        {
            var contents = await client.Repository.Content.GetAllContents(owner, repoName, path);

            foreach (var content in contents)
            {
                if (content.Type == ContentType.File)
                {
                    Console.WriteLine($"File: {content.Path}");
                }
                else if (content.Type == ContentType.Dir)
                {
                    Console.WriteLine($"Directory: {content.Path}");
                    await ProcessDirectory(client, owner, repoName, content.Path);
                }
            }
        }

        public async Task<FolderDiscovery> GetCurrentFolderDetail(string targetDirectory)
        {
            if (string.IsNullOrEmpty(targetDirectory))
                targetDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "k8-workspace"));

            if (!Directory.Exists(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            var result = await GetAllDirectory(targetDirectory);
            result.RootDirectory = Directory.GetCurrentDirectory();
            return result;
        }

        public async Task<List<GitHubContent>> GetFolderDetailService(string targetDirectory)
        {
            List<GitHubContent> gitHubContent = new List<GitHubContent>();
            if (_remoteServerConfig.env == "development")
            {
                if (string.IsNullOrEmpty(targetDirectory))
                    targetDirectory = "local";

                gitHubContent = await GetLinuxFolderDetail(_remoteServerConfig.workingDirectory);
            }

            return gitHubContent;
        }

        public async Task<List<GitHubContent>> GetAllFileService(string targetDirectory)
        {
            List<GitHubContent> gitHubContents = new List<GitHubContent>();
            if (_remoteServerConfig.env == "development")
            {
                if (string.IsNullOrEmpty(targetDirectory))
                {
                    throw new Exception("Invalid location or path passed");
                }

                gitHubContents = await GetLinuxFolderDetail(targetDirectory);
                if (gitHubContents.Count > 0)
                    await GetFilesStatus(gitHubContents);
            }

            return gitHubContents;
        }

        private async Task<List<GitHubContent>> GetFilesStatus(List<GitHubContent> gitHubContents)
        {
            try
            {
                foreach (var gitHubContent in gitHubContents)
                {
                    string extension = Path.GetExtension(gitHubContent.Name);
                    if (extension.Equals(".yml") || extension.Equals(".yaml"))
                    {
                        _logger.LogInformation($"File name: {gitHubContent.Name}");
                        YamlModel yamlModel = await _yamlUtilService.GetGithubYamlFile(gitHubContent.DownloadUrl);

                        string serviceName = yamlModel.Metadata.Name;
                        _logger.LogInformation($"Service name: {serviceName}");

                        gitHubContent.FileType = yamlModel.Kind;
                        switch (yamlModel.Kind.ToUpper())
                        {
                            case nameof(FileType.DEPLOYMENT):
                                gitHubContent.Status = await GetPodDetail(serviceName);
                                break;
                            case nameof(FileType.SERVICE):
                                gitHubContent.Status = !string.IsNullOrEmpty(await GetServiceName(serviceName)) ? true : false;
                                break;
                            case nameof(FileType.PERSISTENTVOLUME):
                                gitHubContent.Status = !string.IsNullOrEmpty(await GetPersistanceVolumeStatus(serviceName)) ? true : false;
                                break;
                            case nameof(FileType.PERSISTENTVOLUMECLAIM):
                                gitHubContent.Status = !string.IsNullOrEmpty(await GetPersistanceVolumeClaimStatus(serviceName)) ? true : false;
                                break;
                            case nameof(FileType.NAMESPACE):
                                gitHubContent.Status = !string.IsNullOrEmpty(await GetNamespaceStatus(serviceName)) ? true : false;
                                break;
                            case nameof(FileType.STATEFULSET):
                                gitHubContent.Status = await GetPodDetail(serviceName);
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }

            return await Task.FromResult(gitHubContents);
        }

        private async Task<List<FileDetail>> GetFilesAndFolder(string targetDirectory)
        {
            List<FileDetail> result = null; //await GetAllFilesInDirectory(targetDirectory);

            // Find folders

            if (result == null)
            {
                result = new List<FileDetail> { new FileDetail() };
            }

            string[] subdirectories = Directory.GetDirectories(targetDirectory);
            if (subdirectories != null && subdirectories.Length > 0)
            {
                foreach (var folder in subdirectories)
                {
                    string folderName = "";
                    if (folder.Contains(@"\"))
                        folderName = folder.Split(@"\").Last();
                    else
                        folderName = folder.Split(@"/").Last();

                    result.Add(new FileDetail
                    {
                        FullPath = folder,
                        FileName = folderName,
                        IsFolder = true
                    });
                }
            }

            return result;
        }

        //private async Task<List<FileDetail>> GetAllFilesInDirectory(string targetDirectory)
        //{
        //    var files = new List<FileDetail>();
        //    string[] fileEntries = Directory.GetFiles(targetDirectory);

        //    foreach (string filePath in fileEntries)
        //    {
        //        string extension = Path.GetExtension(filePath);
        //        if (extension.Equals(".yml") || extension.Equals(".yaml"))
        //        {
        //            string fileName = "";
        //            _logger.LogInformation("Reading file name");
        //            if (filePath.Contains(@"\"))
        //                fileName = filePath.Split(@"\").Last();
        //            else
        //                fileName = filePath.Split(@"/").Last();

        //            _logger.LogInformation($"File name: {fileName}");
        //            YamlModel yamlModel = _yamlUtilService.ReadYamlFile(filePath);

        //            string serviceName = yamlModel.Metadata.Name;
        //            _logger.LogInformation($"Service name: {serviceName}");

        //            switch (yamlModel.Kind.ToUpper())
        //            {
        //                case nameof(FileType.DEPLOYMENT):
        //                    files.Add(await GetPodDetail(serviceName, filePath, fileName, yamlModel.Kind));
        //                    break;
        //                case nameof(FileType.SERVICE):
        //                    files.Add(new FileDetail
        //                    {
        //                        FullPath = filePath,
        //                        FileName = fileName,
        //                        Status = !string.IsNullOrEmpty(await GetServiceName(serviceName)) ? true : false,
        //                        FileType = yamlModel.Kind,
        //                        IsFolder = false
        //                    });
        //                    break;
        //                case nameof(FileType.PERSISTENTVOLUME):
        //                    files.Add(new FileDetail
        //                    {
        //                        FullPath = filePath,
        //                        FileName = fileName,
        //                        Status = !string.IsNullOrEmpty(await GetPersistanceVolumeStatus(serviceName)) ? true : false,
        //                        FileType = yamlModel.Kind,
        //                        PVSize = await GetPersistanceVolumeSize(serviceName),
        //                        IsFolder = false
        //                    });
        //                    break;
        //                case nameof(FileType.PERSISTENTVOLUMECLAIM):
        //                    files.Add(new FileDetail
        //                    {
        //                        FullPath = filePath,
        //                        FileName = fileName,
        //                        Status = !string.IsNullOrEmpty(await GetPersistanceVolumeClaimStatus(serviceName)) ? true : false,
        //                        FileType = yamlModel.Kind,
        //                        IsFolder = false
        //                    });
        //                    break;
        //                case nameof(FileType.NAMESPACE):
        //                    files.Add(new FileDetail
        //                    {
        //                        FullPath = filePath,
        //                        FileName = fileName,
        //                        Status = !string.IsNullOrEmpty(await GetNamespaceStatus(serviceName)) ? true : false,
        //                        FileType = yamlModel.Kind,
        //                        IsFolder = false
        //                    });
        //                    break;
        //                case nameof(FileType.STATEFULSET):
        //                    files.Add(await GetPodDetail(serviceName, filePath, fileName, yamlModel.Kind));
        //                    break;
        //            }
        //        }
        //    }

        //    return await Task.FromResult(files);
        //}

        private async Task<bool> GetPodDetail(string serviceName)
        {
            bool podStatus = false;
            PodRootModel podRootModel = await GetPodName(serviceName);
            ItemStatus status = ItemStatus.Unknown;

            if (podRootModel != null)
                status = _podHelper.FindPodStatus(podRootModel, serviceName);

            if (status == ItemStatus.Succeeded || status == ItemStatus.Running)
                podStatus = true;

            return podStatus;
        }

        private string GetFileType(string fileName)
        {
            var file = fileName.Substring(0, fileName.IndexOf("."));
            var splittedFileNamePart = file.Split('-');
            int len = splittedFileNamePart.Length;

            return splittedFileNamePart[len - 1];
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

        private async Task<string> GetServiceName(string serviceName)
        {
            string optional = " | awk '{print $1}'";
            KubectlModel kubectlModel = new KubectlModel
            {
                Command = $"get service {serviceName} {optional}",
                IsMicroK8 = true,
                IsWindow = false
            };
            var result = await _commonService.RunAllCommandService(kubectlModel);
            return result;
        }

        private async Task<string> GetPersistanceVolumeSize(string serviceName)
        {
            string optional = " | awk '{print $3}'";
            KubectlModel kubectlModel = new KubectlModel
            {
                Command = $"get pv {serviceName} {optional}",
                IsMicroK8 = true,
                IsWindow = false
            };
            var result = await _commonService.RunAllCommandService(kubectlModel);
            return result;
        }

        private async Task<string> GetPersistanceVolumeStatus(string serviceName)
        {
            string optional = " | awk '{print $1}'";
            KubectlModel kubectlModel = new KubectlModel
            {
                Command = $"get pv {serviceName} {optional}",
                IsMicroK8 = true,
                IsWindow = false
            };
            var result = await _commonService.RunAllCommandService(kubectlModel);
            return result;
        }

        private async Task<string> GetPersistanceVolumeClaimStatus(string serviceName)
        {
            string optional = "| awk '{print $1}'";
            KubectlModel kubectlModel = new KubectlModel
            {
                Command = $"get pvc {serviceName} {optional}",
                IsMicroK8 = true,
                IsWindow = false
            };
            var result = await _commonService.RunAllCommandService(kubectlModel);
            return result;
        }

        private async Task<string> GetNamespaceStatus(string serviceName)
        {
            string optional = "| awk '{print $1}'";
            KubectlModel kubectlModel = new KubectlModel
            {
                Command = $"get ns {serviceName} {optional}",
                IsMicroK8 = true,
                IsWindow = false
            };
            var result = await _commonService.RunAllCommandService(kubectlModel);
            return result;
        }

        private async Task<PodRootModel> GetPodName(string podName)
        {
            KubectlModel kubectlModel = new KubectlModel
            {
                Command = $"get pods -o json",
                IsMicroK8 = true,
                IsWindow = false
            };

            return await _commonService.RunCommandForPodService(kubectlModel);
        }

        public async Task<string> RunCommandService(KubectlModel kubectlModel)
        {
            var result = await _commonService.RunAllCommandService(kubectlModel);
            return result;
        }
    }
}
