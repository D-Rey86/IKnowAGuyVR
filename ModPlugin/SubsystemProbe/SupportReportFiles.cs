using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace IKnowAGuyVR.SubsystemProbe;

// Pure managed, bounded disk work. Called only after an explicit report request.
internal static class SupportReportFiles
{
    internal const int TailBytes=96*1024;
    internal static string Write(string directory,string snapshot,string assembly,string[] logs,string[] privatePaths)
    {
        Directory.CreateDirectory(directory);
        string name="IKAG-VR-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".zip";
        string final=Path.Combine(directory,name),pending=final+".partial";
        try
        {
            using(var file=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var zip=new ZipArchive(file,ZipArchiveMode.Create))
            {
                string hash;
                try {using var dll=File.OpenRead(assembly);using var sha=SHA256.Create();hash=Convert.ToHexString(sha.ComputeHash(dll));}
                catch(Exception e){hash="Unavailable ("+e.GetType().Name+")";}
                Add(zip,"report.txt",Redact(snapshot+"\nVR DLL SHA256: "+hash+"\n",privatePaths));
                Add(zip,"README.txt","Created locally at your request; nothing was uploaded.\nreport.txt contains a one-time hardware, tracking and settings snapshot, not a performance benchmark.\nNo saves, screenshots, serial numbers, device IDs or account IDs are collected by the snapshot.\nOptional log tails can contain player names, network details or other personal data written by the game or other plugins. Known local paths are replaced, but this is not complete anonymization. Review every file before sharing.\nLogs are limited to the last 96 KiB per file and can describe earlier events than the snapshot. Missing or locked logs do not prevent the report.\n");
                for(int i=0;i<logs.Length;i++)Add(zip,"logs/"+Path.GetFileName(logs[i])+".txt",Redact(Tail(logs[i]),privatePaths));
            }
            File.Move(pending,final);
            return name;
        }
        catch {try{File.Delete(pending);}catch{}throw;}
    }
    private static void Add(ZipArchive zip,string name,string content)
    {using var writer=new StreamWriter(zip.CreateEntry(name,CompressionLevel.Fastest).Open(),new UTF8Encoding(false));writer.Write(content);}
    internal static string Tail(string path)
    {
        try
        {
            using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            long length=file.Length;int count=(int)Math.Min(length,TailBytes);bool truncated=length>count;
            file.Seek(length-count,SeekOrigin.Begin);var bytes=new byte[count];int read=0,n;
            while(read<count&&(n=file.Read(bytes,read,count-read))>0)read+=n;
            string text=Encoding.UTF8.GetString(bytes,0,read);
            if(truncated){int newline=text.IndexOf('\n');text=newline>=0?text.Substring(newline+1):"[No complete line in bounded tail]";}
            return (truncated?"[Earlier log data omitted]\n":"")+text;
        }
        catch(Exception e){return "[Log unavailable: "+e.GetType().Name+"]\n";}
    }
    internal static string Redact(string text,string[] paths)
    {
        foreach(string path in paths)
        {
            if(string.IsNullOrWhiteSpace(path))continue;
            text=text.Replace(path,"<local-path>",StringComparison.OrdinalIgnoreCase)
                .Replace(path.Replace('\\','/'),"<local-path>",StringComparison.OrdinalIgnoreCase)
                .Replace(path.Replace('/','\\'),"<local-path>",StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }
}
