﻿using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using eft_dma_radar.Tarkov.EFTPlayer;
using Open.Nat;
using MessagePack;
using System.Net.Sockets;
using eft_dma_radar.Tarkov.WebRadar.Data;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.MessagePack;
using eft_dma_radar.Tarkov.Loot;
using System.Net.Http;
using Microsoft.AspNetCore.Http;

namespace eft_dma_radar.Tarkov.WebRadar
{
    internal static class WebRadarServer
    {
        private static readonly WebRadarUpdate _update = new();
        private static readonly WaitTimer _waitTimer = new();
        private static TimeSpan _tickRate;
        private static IHost _webHost;

        /// <summary>
        /// Password for this Server.
        /// </summary>
        private static string _password = Utils.GetRandomPassword(10);
        public static string Password => _password;

        #region Public API
        /// <summary>
        /// Startup web server for Web Radar.
        /// </summary>
        /// <param name="ip">IP to bind to.</param>
        /// <param name="port">TCP Port to bind to.</param>
        /// <param name="tickRate">How often radar updates should be broadcast.</param>
        /// <param name="upnp">True if Port Forwarding should be setup via UPnP.</param>
        /// <summary>
        /// Startup web server for Web Radar.
        /// </summary>
        /// <param name="ip">IP to bind to.</param>
        /// <param name="port">TCP Port to bind to.</param>
        /// <param name="tickRate">How often radar updates should be broadcast.</param>
        /// <param name="upnp">True if Port Forwarding should be setup via UPnP.</param>
        public static async Task StartAsync(string ip, int port, TimeSpan tickRate, bool upnp)
        {
            _tickRate = tickRate;
            ThrowIfInvalidBindParameters(ip, port);
            if (upnp)
                await ConfigureUPnPAsync(port);
            _webHost = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel()
                        .ConfigureServices(services =>
                        {
                            services.AddSignalR(options =>
                            {
                                options.MaximumReceiveMessageSize = 1024 * 128; // Set the maximum message size to 128KB
                            })
                            .AddMessagePackProtocol(options =>
                            {
                                options.SerializerOptions = MessagePackSerializerOptions.Standard
                                    .WithSecurity(MessagePackSecurity.TrustedData)
                                    .WithCompression(MessagePackCompression.Lz4BlockArray)
                                    .WithResolver(ResolverGenerator.Instance);
                            });
                            services.AddCors(options =>
                            {
                                options.AddDefaultPolicy(builder =>
                                {
                                    builder.AllowAnyOrigin()
                                           .AllowAnyHeader()
                                           .AllowAnyMethod()
                                           .SetIsOriginAllowedToAllowWildcardSubdomains();
                                });
                            });
                        })
                        .Configure(app =>
                        {
                            app.UseCors();
                            app.UseRouting();
                            app.UseEndpoints(endpoints =>
                            {
                                endpoints.MapHub<RadarServerHub>("/hub/0f908ff7-e614-6a93-60a3-cee36c9cea91");
                                endpoints.MapGet("/{path}", async (HttpContext context, string path) =>
                                {
                                    try
                                    {
                                        var client = new HttpClient();
                                        byte[] imageBytes = await client.GetByteArrayAsync($"https://assets.tarkov.dev/{path}");
                                        context.Response.Headers.CacheControl = $"public,max-age=604800";
                                        return TypedResults.Bytes(imageBytes, "image/webp");
                                    }
                                    catch (Exception e)
                                    {
                                        LoneLogging.WriteLine($"API ERROR: {e.Message}");
                                        return Results.Problem(e.Message);
                                    }
                                });
                            });
                        })
                        .UseUrls($"http://{FormatIPForURL(ip)}:{port}");
                })
                .Build();

            _webHost.Start();

            // Start the background worker
            new Thread(Worker)
            {
                IsBackground = true
            }.Start();
        }

        /// <summary>
        /// Checks if the specified IP Address / Port Number are valid, and throws an exception if they are invalid.
        /// Performs a TCP Bind Test.
        /// </summary>
        /// <param name="ip">IP to test bind.</param>
        /// <param name="port">Port to test bind.</param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="Exception"></exception>
        private static void ThrowIfInvalidBindParameters(string ip, int port)
        {
            try
            {
                if (port is < 1024 or > 65535)
                    throw new ArgumentException("Invalid Port. We recommend using a Port between 50000-60000.");
                var ipObj = IPAddress.Parse(ip);
                using var socket = new Socket(ipObj.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(ipObj, port));
                socket.Close();
            }
            catch (SocketException ex)
            {
                throw new Exception($"Invalid Bind Parameters. Use your Radar PC's Local LAN IP (example: 192.168.1.100), and a port number between 50000-60000.\n" +
                    $"SocketException: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the External IP of the user running the Server.
        /// </summary>
        /// <returns>External WAN IP.</returns>
        /// <exception cref="Exception"></exception>
        public static async Task<string> GetExternalIPAsync()
        {
            var errors = new StringBuilder();

            try
            {
                string ip = null;

                try
                {
                    ip = await QueryUPnPForIPAsync();

                    if (!string.IsNullOrWhiteSpace(ip))
                        return ip;
                }
                catch (Exception ex)
                {
                    errors.AppendLine($"[UPnP Error] {ex.Message}");
                }

                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        httpClient.Timeout = TimeSpan.FromSeconds(5);

                        var ipServices = new[]
                        {
                            "https://api.ipify.org",
                            "https://icanhazip.com",
                            "https://ifconfig.me/ip"
                        };

                        foreach (var service in ipServices)
                        {
                            try
                            {
                                var response = await httpClient.GetStringAsync(service);
                                ip = response.Trim();

                                if (IPAddress.TryParse(ip, out _))
                                    return ip;
                            }
                            catch (Exception ex)
                            {
                                errors.AppendLine($"[Service {service} Error] {ex.Message}");
                                continue;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.AppendLine($"[HTTP Error] {ex.Message}");
                }

                if (string.IsNullOrWhiteSpace(ip))
                    throw new Exception("Failed to obtain external IP address from any source");

                return ip;
            }
            catch (Exception ex)
            {
                errors.AppendLine($"[Final Error] {ex.Message}");
                throw new Exception($"ERROR Getting External IP: {errors}");
            }
        }

        /// <summary>
        /// Get the local LAN IPv4 address of this machine.
        /// </summary>
        /// <returns>Local LAN IP address, or null if not found.</returns>
        public static string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());

                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        var bytes = ip.GetAddressBytes();

                        if (IsPrivateIP(bytes))
                            return ip.ToString();
                    }
                }

                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }

                return null;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[GetLocalIP] Error: {ex.Message}");
                return null;
            }
        }

        public static void OverridePassword(string newPassword)
        {
            if (!string.IsNullOrWhiteSpace(newPassword))
                _password = newPassword;
        }
        #endregion

        #region Private API
        /// <summary>
        /// Web Radar Server Worker Thread.
        /// </summary>
        private static async void Worker()
        {
            var hubContext = _webHost.Services.GetRequiredService<IHubContext<RadarServerHub>>();
            var tickRate = _tickRate;
        
            while (true)
            {
                try
                {
                    // Players
                    if (Memory.InRaid && Memory.Players is IReadOnlyCollection<Player> players && players.Count > 0)
                    {
                        _update.InGame = true;
                        _update.MapID = Memory.MapID;
                        _update.Players = players.Select(p => WebRadarPlayer.CreateFromPlayer(p));

                        // Loot
                        if (Memory.Loot?.UnfilteredLoot is IReadOnlyCollection<LootItem> loot && loot.Count > 0)
                        {
                            _update.Loot = loot.Select(l => WebRadarLoot.CreateFromLoot(l));
                            //LoneLogging.WriteLine($"[WebRadar] Found {loot.Count} loot items.");
                        }
                        else
                        {
                            _update.Loot = null;
                        }

                        if (Memory.Game?.Interactables != null)
                        {
                            _update.Doors = Memory.Game?.Interactables._Doors?.Select(x => WebRadarDoor.CreateFromDoor(x));
                            int doorsCount = Memory.Game?.Interactables._Doors?.Count ?? 0;
                            //LoneLogging.WriteLine($"[WebRadar] Found {doorsCount} doors.");
                        }
                    }
                    else
                    {
                        _update.InGame = false;
                        _update.MapID = null;
                        _update.Players = null;
                        _update.Loot = null;
                    }

                    _update.Version++;

                    // Send update to all connected clients
                    await hubContext.Clients.All.SendAsync("RadarUpdate", _update);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Worker] Error: {ex.Message}");
                }
        
                // Wait for the next update
                _waitTimer.AutoWait(tickRate);
            }
        }

        /// <summary>
        /// Formats an IP Host string for use in a URL.
        /// </summary>
        /// <param name="host">IP/Hostname to check/format.</param>
        /// <returns>Formatted IP, or original string if no formatting is needed.</returns>
        private static string FormatIPForURL(string host)
        {
            if (host is null)
                return null;
            if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily is AddressFamily.InterNetworkV6)
                return $"[{host}]";
            return host;
        }

        /// <summary>
        /// Get the Nat Device for the local UPnP Service.
        /// </summary>
        /// <returns>Task with NatDevice object.</returns>
        private async static Task<NatDevice> GetNatDeviceAsync()
        {
            var dsc = new NatDiscoverer();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            return await dsc.DiscoverDeviceAsync(PortMapper.Upnp, cts);
        }

        /// <summary>
        /// Attempts to setup UPnP Port Forwarding for the specified port.
        /// </summary>
        /// <param name="port">Port to forward.</param>
        /// <returns>Task with result of operation.</returns>
        /// <exception cref="Exception"></exception>
        private static async Task ConfigureUPnPAsync(int port)
        {
            try
            {
                var upnp = await GetNatDeviceAsync();

                // Create New Mapping
                await upnp.CreatePortMapAsync(new Mapping(Protocol.Tcp, port, port, 86400, "Lone Web Radar"));
            }
            catch (Exception ex)
            {
                throw new Exception($"ERROR Setting up UPnP: {ex.Message}");
            }
        }

        /// <summary>
        /// Lookup the External IP Address via UPnP.
        /// </summary>
        /// <returns>External IP Address.</returns>
        private static async Task<string> QueryUPnPForIPAsync()
        {
            var upnp = await GetNatDeviceAsync();
            var ip = await upnp.GetExternalIPAsync();
            return ip.ToString();
        }

        /// <summary>
        /// Check if an IP address is in a private network range.
        /// </summary>
        /// <param name="ip">IP address bytes.</param>
        /// <returns>True if private IP, false otherwise.</returns>
        private static bool IsPrivateIP(byte[] ip)
        {
            if (ip[0] == 192 && ip[1] == 168)
                return true;

            if (ip[0] == 10)
                return true;

            if (ip[0] == 172 && (ip[1] >= 16 && ip[1] <= 31))
                return true;

            return false;
        }

        private sealed class RadarServerHub : Hub
        {
            public override async Task OnConnectedAsync()
            {
                var httpContext = Context.GetHttpContext();

                string password = httpContext?.Request?.Query?["password"].ToString() ?? "";
                if (password != Password)
                {
                    LoneLogging.WriteLine($"WebRadar Unauthorized Connection Attempt: {httpContext.Connection.RemoteIpAddress}");
                    return;
                }

                await base.OnConnectedAsync();
            }
        }

        #endregion   
    }
}
