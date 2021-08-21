﻿using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OpenBullet2.Core.Entities;
using OpenBullet2.Core.Models.Jobs;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Jobs;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenBullet2.Native.ViewModels
{
    public class JobsViewModel : ViewModelBase
    {
        private readonly IJobRepository jobRepo;
        private readonly JobManagerService jobManager;
        private readonly JobFactoryService jobFactory;

        private ObservableCollection<JobViewModel> jobsCollection;
        public ObservableCollection<JobViewModel> JobsCollection
        {
            get => jobsCollection;
            set
            {
                jobsCollection = value;
                OnPropertyChanged();
            }
        }

        public JobsViewModel()
        {
            jobRepo = SP.GetService<IJobRepository>();
            jobManager = SP.GetService<JobManagerService>();
            jobFactory = SP.GetService<JobFactoryService>();

            CreateCollection();
        }

        private void CreateCollection()
            => JobsCollection = new ObservableCollection<JobViewModel>(jobManager.Jobs.Select(j => MakeViewModel(j)));

        public async Task CreateJob(JobOptions options)
        {
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var wrapper = new JobOptionsWrapper { Options = options };

            var entity = new JobEntity
            {
                CreationDate = DateTime.Now,
                JobType = GetJobType(options),
                JobOptions = JsonConvert.SerializeObject(wrapper, settings)
            };

            await jobRepo.Add(entity);

            var job = jobFactory.FromOptions(entity.Id, 0, options);

            jobManager.Jobs.Add(job);
            JobsCollection.Add(MakeViewModel(job));
        }

        public async Task EditJob(JobEntity entity, JobOptions options)
        {
            var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var wrapper = new JobOptionsWrapper { Options = options };
            entity.JobOptions = JsonConvert.SerializeObject(wrapper, jsonSettings);

            await jobRepo.Update(entity);

            var oldJob = jobManager.Jobs.First(j => j.Id == entity.Id);
            var newJob = jobFactory.FromOptions(entity.Id, 0, options);

            jobManager.Jobs.Remove(oldJob);
            jobManager.Jobs.Add(newJob);

            CreateCollection();
        }

        public async Task CloneJob(JobType type, JobOptions options)
        {
            var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var wrapper = new JobOptionsWrapper { Options = options };
            var entity = new JobEntity
            {
                CreationDate = DateTime.Now,
                JobType = type,
                JobOptions = JsonConvert.SerializeObject(wrapper, jsonSettings)
            };

            await jobRepo.Add(entity);

            var job = jobFactory.FromOptions(entity.Id, 0, options);
            jobManager.Jobs.Add(job);

            JobViewModel jobVM = type switch
            {
                JobType.MultiRun => new MultiRunJobViewModel(job as MultiRunJob),
                JobType.ProxyCheck => new ProxyCheckJobViewModel(job as ProxyCheckJob),
                _ => throw new NotImplementedException()
            };

            JobsCollection.Add(jobVM);
        }

        public void RemoveAll()
        {
            var notIdleJobs = jobManager.Jobs.Where(j => j.Status != JobStatus.Idle);

            if (notIdleJobs.Any())
            {
                throw new Exception($"The job #{notIdleJobs.First().Id} is not idle, please stop/abort the job first!");
            }

            // If admin, just purge all
            jobRepo.Purge();
            jobManager.Jobs.Clear();
            JobsCollection.Clear();
        }

        public async Task RemoveJob(JobViewModel jobVM)
        {
            if (jobVM.Job.Status != JobStatus.Idle)
            {
                throw new Exception("The job is not idle, please stop/abort the job first!");
            }

            var entity = await jobRepo.GetAll().FirstAsync(e => e.Id == jobVM.Id);
            await jobRepo.Delete(entity);
            jobManager.Jobs.Remove(jobVM.Job);
            JobsCollection.Remove(jobVM);
        }

        private static JobViewModel MakeViewModel(Job job) => job switch
        {
            MultiRunJob mr => new MultiRunJobViewModel(mr),
            ProxyCheckJob pc => new ProxyCheckJobViewModel(pc),
            _ => throw new NotImplementedException()
        };

        private static JobType GetJobType(JobOptions options) => options switch
        {
            MultiRunJobOptions => JobType.MultiRun,
            ProxyCheckJobOptions => JobType.ProxyCheck,
            _ => throw new NotImplementedException()
        };

        private static JobType GetJobType(Job job) => job switch
        {
            MultiRunJob => JobType.MultiRun,
            ProxyCheckJob => JobType.ProxyCheck,
            _ => throw new NotImplementedException()
        };
    }

    public class JobViewModel : ViewModelBase
    {
        public Job Job { get; init; }

        public string IdAndStatus => $"#{Id} [{Status}]";
        public int Id => Job.Id;
        public JobStatus Status => Job.Status;

        public JobViewModel(Job job)
        {
            Job = job;
        }
    }

    public class MultiRunJobViewModel : JobViewModel
    {
        private MultiRunJob MultiRunJob => Job as MultiRunJob;

        public string ConfigName => MultiRunJob.Config is null ? "No config" : MultiRunJob.Config.Metadata.Name;
        public string DataPoolInfo => MultiRunJob.DataPool switch
        {
            WordlistDataPool w => $"{w.Wordlist.Name} (Wordlist)",
            CombinationsDataPool => "Combinations",
            InfiniteDataPool => "Infinite",
            RangeDataPool => "Range",
            FileDataPool f => $"{Path.GetFileName(f.FileName)} (File)",
            _ => throw new System.NotImplementedException()
        };

        public int Bots => MultiRunJob.Bots;
        public int Skip => MultiRunJob.Skip;
        public JobProxyMode ProxyMode => MultiRunJob.ProxyMode;

        // Stats
        public int DataTested => MultiRunJob.DataTested;
        public int DataHits => MultiRunJob.DataHits;
        public int DataCustom => MultiRunJob.DataCustom;
        public int DataToCheck => MultiRunJob.DataToCheck;
        public int DataFails => MultiRunJob.DataFails;
        public int DataRetried => MultiRunJob.DataRetried;
        public int DataBanned => MultiRunJob.DataBanned;
        public int DataErrors => MultiRunJob.DataErrors;
        public int DataInvalid => MultiRunJob.DataInvalid;

        // Proxy stats
        public int ProxiesAlive => MultiRunJob.ProxiesAlive;
        public int ProxiesBanned => MultiRunJob.ProxiesBanned;
        public int ProxiesBad => MultiRunJob.ProxiesBad;
        public int ProxiesTotal => MultiRunJob.ProxiesTotal;

        public float Progress => MultiRunJob.Progress;
        public string ProgressString => $"{DataTested + Skip} / {MultiRunJob.DataPool.Size} ({(Progress == -1 ? 0 : Progress * 100):0.00}%)";

        public decimal CaptchaCredit => MultiRunJob.CaptchaCredit;
        public string ElapsedString => $"{(int)MultiRunJob.Elapsed.TotalDays} day(s) {MultiRunJob.Elapsed:hh\\:mm\\:ss}";
        public string RemainingString => $"{(int)MultiRunJob.Remaining.TotalDays} day(s) {MultiRunJob.Remaining:hh\\:mm\\:ss}";

        public int CPM => MultiRunJob.CPM;

        public MultiRunJobViewModel(MultiRunJob job) : base(job)
        {

        }
    }

    public class ProxyCheckJobViewModel : JobViewModel
    {
        private ProxyCheckJob ProxyCheckJob => Job as ProxyCheckJob;

        public int Bots => ProxyCheckJob.Bots;
        public int Total => ProxyCheckJob.Total;
        public int Tested => ProxyCheckJob.Tested;
        public int Working => ProxyCheckJob.Working;
        public int NotWorking => ProxyCheckJob.NotWorking;

        public float Progress => ProxyCheckJob.Progress;
        public string ProgressString => $"{Tested} / {Total} ({(Progress == -1 ? 0 : Progress * 100):0.00}%)";

        public int CPM => ProxyCheckJob.CPM;

        public ProxyCheckJobViewModel(ProxyCheckJob job) : base(job)
        {

        }
    }
}
