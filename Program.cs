using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Toolbelt.Blazor.Extensions.DependencyInjection;
using Blazored.LocalStorage;
using atomex_frontend.Storages;
using System.Net.Http;
using Plk.Blazor.DragDrop;

namespace atomex_frontend
{
  public class Program
  {
    public static async Task Main(string[] args)
    {
      var builder = WebAssemblyHostBuilder.CreateDefault(args);
      builder.RootComponents.Add<App>("app");

      builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
      builder.Services.AddI18nText();
      builder.Services.AddBlazoredLocalStorage();
      builder.Services.AddBlazorDragDrop();
      builder.Services.AddScoped<AccountStorage, AccountStorage>();
      builder.Services.AddScoped<UserStorage, UserStorage>();
      builder.Services.AddScoped<RegisterStorage, RegisterStorage>();
      builder.Services.AddScoped<WalletStorage, WalletStorage>();
      builder.Services.AddScoped<SwapStorage, SwapStorage>();
      builder.Services.AddScoped<BakerStorage, BakerStorage>();

      await builder
        .Build()
        .UseLocalTimeZone()
        .RunAsync();
    }
  }

  public static class EnumExtensions
  {
    public static T GetAttribute<T>(this Enum value) where T : Attribute
    {
      var type = value.GetType();
      var memberInfo = type.GetMember(value.ToString());
      if (memberInfo.Length == 0)
      {
        return null;
      }
      var attributes = memberInfo[0].GetCustomAttributes(typeof(T), false);
      return attributes.Length > 0
        ? (T)attributes[0]
        : null;
    }

    public static string ToName(this Enum value)
    {
      var attribute = value.GetAttribute<DescriptionAttribute>();
      return attribute == null ? value.ToString() : attribute.Description;
    }

    public static T Next<T>(this T src) where T : struct
    {
      if (!typeof(T).IsEnum) throw new ArgumentException(String.Format("Argument {0} is not an Enum", typeof(T).FullName));

      T[] Arr = (T[])Enum.GetValues(src.GetType());
      int j = Array.IndexOf<T>(Arr, src) + 1;
      return (Arr.Length == j) ? Arr[0] : Arr[j];
    }

    public static T Previous<T>(this T src) where T : struct
    {
      if (!typeof(T).IsEnum) throw new ArgumentException(String.Format("Argument {0} is not an Enum", typeof(T).FullName));

      T[] Arr = (T[])Enum.GetValues(src.GetType());
      int j = Array.IndexOf<T>(Arr, src) - 1;
      return (Arr.Length == j) ? Arr[0] : Arr[j];
    }

    private static Random rng = new Random();

    public static void Shuffle<T>(this IList<T> list)
    {
      int n = list.Count;
      while (n > 1)
      {
        n--;
        int k = rng.Next(n + 1);
        T value = list[k];
        list[k] = list[n];
        list[n] = value;
      }
    }
  }
}
