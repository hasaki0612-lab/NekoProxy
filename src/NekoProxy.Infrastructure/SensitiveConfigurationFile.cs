using System.Text;
namespace NekoProxy.Infrastructure;
public static class SensitiveConfigurationFile
{
    public static async Task SaveAsync(string path,string contents)
    {
        var full=Path.GetFullPath(path);var temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
        if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
        try
        {
            await using(var stream=new FileStream(temp,options))
            await using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))await writer.WriteAsync(contents);
            File.Move(temp,full,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
