using Microsoft.AspNetCore.Components.Builder;
using Microsoft.Extensions.DependencyInjection;
using Toolbelt.Blazor.Extensions.DependencyInjection;
using Blazored.LocalStorage;
using atomex_frontend.Storages;
using System;
using System.ComponentModel;

namespace atomex_frontend
{
  public class Startup
  {
    public void ConfigureServices(IServiceCollection services)
    {
      services.AddI18nText();
      services.AddBlazoredLocalStorage();
      services.AddSingleton<AccountStorage, AccountStorage>();
      services.AddSingleton<UserStorage, UserStorage>();
      services.AddSingleton<RegisterStorage, RegisterStorage>();
    }

    public void Configure(IComponentsApplicationBuilder app)
    {
      app.AddComponent<App>("app");
    }
  }


  public static class EnumExtensions
  {
    public static T GetAttribute<T>(this Enum value) where T : Attribute
    {
      var type = value.GetType();
      var memberInfo = type.GetMember(value.ToString());
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
  }
}
