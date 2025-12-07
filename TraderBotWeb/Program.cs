using Microsoft.Extensions.DependencyInjection;
using TraderBotWeb.Components;

namespace TraderBotWeb
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			builder.Services.AddServerSideBlazor()
					.AddHubOptions(options =>
					{
						// Send keep-alive pings every 10 seconds
						options.KeepAliveInterval = TimeSpan.FromSeconds(10);

						// Wait 60 seconds before timing out the client
						options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);

						// Increase handshake timeout
						options.HandshakeTimeout = TimeSpan.FromSeconds(30);
					});

			builder.Services.AddServerSideBlazor()
						.AddCircuitOptions(options =>
						{
							options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30);
							options.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds(60);
						});

			// Add services to the container.
			builder.Services.AddRazorComponents()
				.AddInteractiveServerComponents();

			var app = builder.Build();

			// Configure the HTTP request pipeline.
			if (!app.Environment.IsDevelopment())
			{
				app.UseExceptionHandler("/Error");
				// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
				app.UseHsts();
			}

			app.UseHttpsRedirection();

			app.UseStaticFiles();
			app.UseAntiforgery();

			app.MapRazorComponents<App>()
				.AddInteractiveServerRenderMode();

			app.Run();
		}
	}
}
