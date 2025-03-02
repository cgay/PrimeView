﻿using Microsoft.Extensions.Configuration;
using PrimeView.Entities;
using PrimeView.RestAPIReader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PrimeView.RestAPIReader
{
	public class ReportReader : IReportReader
	{
		private readonly Dictionary<string, SortedList<int, ReportSummary>> summaryMap = new();
		private readonly Dictionary<string, Report> reportMap = new();
		private readonly Service.PrimesAPI primesAPI;
		private readonly Dictionary<string, int> totalReportsMap = new();

		public ReportReader(IConfiguration configuration)
		{
			this.primesAPI = new(new HttpClient());
			if (!string.IsNullOrEmpty(configuration[Constants.APIBaseURI]))
				this.primesAPI.BaseUrl = configuration.GetValue<string>(Constants.APIBaseURI);
		}

		private async Task<(SortedList<int, ReportSummary> summaries, int totalReports)> LoadMissingSummaries(string? runnerId, int skipFirst, int maxSummaryCount)
		{
			if (runnerId == null)
				runnerId = string.Empty;

			if (!this.summaryMap.ContainsKey(runnerId))
				this.summaryMap.Add(runnerId, new SortedList<int, ReportSummary>());
			
			var summaries = summaryMap[runnerId];

            for (int missingIndex = skipFirst; missingIndex < skipFirst + maxSummaryCount; missingIndex++)
			{
				// find gaps in the requested key space, and fill them
				if (!summaries.ContainsKey(missingIndex))
				{
					int missingCount = 0;

					// count number of missing keys, but stop when we've reached the end of the key space we were asked to load
					while (!summaries.ContainsKey(missingIndex + ++missingCount) && (missingIndex + missingCount) < (skipFirst + maxSummaryCount));

					await LoadSummaries(summaries, runnerId, missingIndex, missingCount);

					// we may not actually have been able to load all requested missing summaries, but for the sake of filling gaps
					//  for which data is available in an efficient manner, we'll act like we did; it just means some gaps may remain
					missingIndex += missingCount;
				}
			}

			return (summaries, this.totalReportsMap[runnerId]);
		}

		private async Task LoadSummaries(SortedList<int, ReportSummary> summaries, string runnerId, int skipFirst, int maxSummaryCount)
		{
			Service.Sessions sessionsResult;
			try
			{
				if (string.IsNullOrWhiteSpace(runnerId) || !int.TryParse(runnerId, out int parsedRunnerId))
					sessionsResult = await this.primesAPI.GetSessionsAsync(skipFirst, maxSummaryCount, null);
				else
					sessionsResult = await this.primesAPI.GetRunnerSessionsAsync(parsedRunnerId, skipFirst, maxSummaryCount);
			}
			catch (Service.ApiException e)
			{
				Console.Error.WriteLine(e);
				return;
			}

			int i = 0;
			foreach (var session in sessionsResult.Data)
			{
				var runner = session.Runner;

				ReportSummary summary = new()
				{
					Id = session.Id,
					Date = session.Created_at.DateTime,
					User = runner.Name,
					Architecture = runner.Os_arch,
					CpuBrand = runner.Cpu_brand,
					CpuCores = (int)runner.Cpu_cores,
					CpuProcessors = (int)runner.Cpu_processors,
					CpuVendor = runner.Cpu_vendor,
					DockerArchitecture = runner.Docker_architecture,
					IsSystemVirtual = runner.System_virtual,
					OsPlatform = runner.Os_platform,
					OsDistro = runner.Os_distro,
					OsRelease = runner.Os_release,
					ResultCount = (int)session.Results_count
				};

				summaries.Add(skipFirst + i++, summary);
			}

			this.totalReportsMap[runnerId] = sessionsResult.Total;
		}

		public async Task<Report> GetReport(string id)
		{
			if (this.reportMap.ContainsKey(id))
				return this.reportMap[id];

			Service.Session? sessionResponse;
			try
			{
				sessionResponse = await this.primesAPI.GetSessionResultsAsync(int.Parse(id));
			}
			catch (Service.ApiException e)
			{
				Console.Error.WriteLine(e);
				return new Report();
			}

			(CPUInfo cpu, SystemInfo system, OperatingSystemInfo operatingSystem, DockerInfo dockerInfo) = ParseRunner(sessionResponse.Runner);

			List<Result> results = new();
			foreach(var apiResult in sessionResponse.Results)
			{
				Result result = new()
				{
					Algorithm = apiResult.Algorithm,
					Duration = apiResult.Duration,
					IsFaithful = apiResult.Faithful,
					Label = apiResult.Label,
					Language = apiResult.Implementation,
					Passes = (long)apiResult.Passes,
					Solution = apiResult.Solution,
					Threads = apiResult.Threads
				};

				if (int.TryParse(apiResult.Bits, out int bits))
					result.Bits = bits;

				results.Add(result);
			}

			Report report = new()
			{
				Id = sessionResponse.Id,
				Date = sessionResponse.Created_at.DateTime,
				User = sessionResponse.Runner.Name,
				CPU = cpu,
				System = system,
				OperatingSystem = operatingSystem,
				DockerInfo = dockerInfo,
				Results = results.ToArray()
			};

			this.reportMap[id] = report;

			return report;
		}

		public async Task<Runner[]> GetRunners()
		{
			Service.Runners runnersResponse;

			try
			{
				runnersResponse = await this.primesAPI.GetRunnersAsync(0, 100);
			}
			catch (Service.ApiException e)
			{
				Console.Error.WriteLine(e);
                return Array.Empty<Runner>();
            }

			List<Runner> runners = new();

			foreach (var runner in runnersResponse.Data)
			{
                (CPUInfo cpu, SystemInfo system, OperatingSystemInfo operatingSystem, DockerInfo dockerInfo) = ParseRunner(runner);

				runners.Add(new()
				{
					Id = runner.Id,
					User = runner.Name,
					CPU = cpu,
					System = system,
					OperatingSystem = operatingSystem,
					DockerInfo = dockerInfo
				});
			}

			return runners.ToArray();
        }

        private (CPUInfo cpu, SystemInfo system, OperatingSystemInfo operatingSystem, DockerInfo dockerInfo) ParseRunner(Service.Runner runner)
		{
            CPUInfo cpu = new()
            {
                Brand = runner.Cpu_brand,
                Cores = (int)runner.Cpu_cores,
                EfficiencyCores = (int?)runner.Cpu_efficiency_cores,
                Family = runner.Cpu_family,
                Flags = runner.Cpu_flags,
                Governor = runner.Cpu_governor,
                Manufacturer = runner.Cpu_manufacturer,
                MaximumSpeed = (float?)runner.Cpu_speed_max,
                MinimumSpeed = (float?)runner.Cpu_speed_min,
                Model = runner.Cpu_model,
                PerformanceCores = (int?)runner.Cpu_performance_cores,
                PhysicalCores = (int)runner.Cpu_physical_cores,
                Processors = (int)runner.Cpu_processors,
                RaspberryProcessor = runner.System_raspberry_processor,
                Revision = runner.Cpu_revision,
                Socket = runner.Cpu_socket,
                Speed = (float?)runner.Cpu_speed,
                Stepping = runner.Cpu_stepping,
                Vendor = runner.Cpu_vendor,
                Virtualization = runner.Cpu_virtualization,
                Voltage = runner.Cpu_voltage
            };

            Dictionary<string, object> cache = new();

            if (runner.Cpu_cache_l1d != null)
                cache["l1d"] = (long)runner.Cpu_cache_l1d;
            if (runner.Cpu_cache_l1i != null)
                cache["l1i"] = (long)runner.Cpu_cache_l1i;
            if (runner.Cpu_cache_l2 != null)
                cache["l2"] = (long)runner.Cpu_cache_l2;
            if (runner.Cpu_cache_l3 != null)
                cache["l3"] = (long)runner.Cpu_cache_l3;

            if (cache.Count > 0)
                cpu.Cache = cache;

            SystemInfo system = new()
            {
                IsVirtual = runner.System_virtual,
                Manufacturer = runner.System_manufacturer,
                Model = runner.System_model,
                RaspberryManufacturer = runner.System_raspberry_manufacturer,
                RaspberryRevision = runner.System_raspberry_revision,
                RaspberryType = runner.System_raspberry_type,
                SKU = runner.System_sku,
                Version = runner.System_version
            };

            OperatingSystemInfo operatingSystem = new()
            {
                Architecture = runner.Os_arch,
                Build = runner.Os_build,
                CodeName = runner.Os_codename,
                CodePage = runner.Os_codepage,
                Distribution = runner.Os_distro,
                IsUefi = runner.Os_uefi,
                Kernel = runner.Os_kernel,
                LogoFile = runner.Os_logofile,
                Platform = runner.Os_platform,
                Release = runner.Os_release,
                ServicePack = runner.Os_servicepack
            };

            DockerInfo dockerInfo = new()
            {
                Architecture = runner.Docker_architecture,
                CPUCount = (int)runner.Docker_ncpu,
                KernelVersion = runner.Docker_kernel_version,
                OperatingSystem = runner.Docker_operating_system,
                OSType = runner.Docker_os_type,
                OSVersion = runner.Docker_os_version,
                ServerVersion = runner.Docker_server_version,
                TotalMemory = (long)runner.Docker_mem_total
            };

			return (cpu, system, operatingSystem, dockerInfo);
        }

        public async Task<(ReportSummary[] summaries, int total)> GetSummaries(int maxSummaryCount)
		{
			return await GetSummaries(null, 0, maxSummaryCount);
		}

		public async Task<(ReportSummary[] summaries, int total)> GetSummaries(string? runnerId, int skipFirst, int maxSummaryCount)
		{
			var result = await LoadMissingSummaries(string.IsNullOrWhiteSpace(runnerId) ? null : runnerId, skipFirst, maxSummaryCount);

			return (result.summaries.SkipWhile(pair => pair.Key < skipFirst).TakeWhile(pair => pair.Key < skipFirst + maxSummaryCount).Select(pair => pair.Value).ToArray(), result.totalReports);
		}

		public void FlushCache()
		{
			this.summaryMap.Clear();
			this.reportMap.Clear();
		}
	}
}
