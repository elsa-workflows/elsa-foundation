using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using System.Net;

if(args[0]=="silo")
{
 var port=int.Parse(args[1]);
 using var host=Host.CreateDefaultBuilder().ConfigureLogging(l=>l.SetMinimumLevel(LogLevel.Warning))
  .UseOrleans(s=>s.UseLocalhostClustering(siloPort:port,gatewayPort:port+100,primarySiloEndpoint:new IPEndPoint(IPAddress.Loopback,19111),serviceId:"elsa-evidence",clusterId:"elsa-preflight"))
  .Build();
 await host.StartAsync();
 Console.WriteLine("SILO_READY "+port);
 await host.WaitForShutdownAsync();
}
else
{
 using var host=Host.CreateDefaultBuilder().ConfigureLogging(l=>l.SetMinimumLevel(LogLevel.Warning))
  .UseOrleansClient(c=>c.UseLocalhostClustering(gatewayPorts:args.Length > 2 ? new[]{int.Parse(args[2])} : new[]{19211,19212},serviceId:"elsa-evidence",clusterId:"elsa-preflight"))
  .Build();
 await host.StartAsync();
 var client=host.Services.GetRequiredService<IClusterClient>();
 var grain=client.GetGrain<IProbe>(args.Length > 1 ? args[1] : "same-execution");
 Console.WriteLine("GRAIN_LOCATION "+await grain.Ping());
 await host.StopAsync();
}
public interface IProbe:IGrainWithStringKey { Task<string> Ping(); }
[Orleans.Placement.PreferLocalPlacement]
public sealed class ProbeGrain:Grain,IProbe
{
 public Task<string> Ping()=>Task.FromResult($"{this.GetPrimaryKeyString()} pid={Environment.ProcessId}");
}
