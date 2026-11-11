using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using IKnowAGuyVR.SubsystemProbe;

var root=Path.Combine(Path.GetTempPath(),"IKAG-ReportChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
try
{
    string dll=typeof(SupportReportFiles).Assembly.Location;
    string log=Path.Combine(root,"test.log");File.WriteAllText(log,new string('x',150000)+"\nC:\\Users\\TestPerson\\private\nlatest event\n");
    var paths=new[]{"C:\\Users\\TestPerson"};
    Check(!SupportReportFiles.Redact("c:/users/testperson/test C:\\USERS\\TESTPERSON\\test",paths).Contains("testperson",StringComparison.OrdinalIgnoreCase),"case and separators redacted");
    Check(SupportReportFiles.Tail(log).Length<SupportReportFiles.TailBytes+100,"bounded tail");
    Check(SupportReportFiles.Tail(log).Contains("latest event"),"tail keeps recent events");
    string first=SupportReportFiles.Write(root,"snapshot",dll,Array.Empty<string>(),paths);
    using(var zip=ZipFile.OpenRead(Path.Combine(root,first))){Check(zip.Entries.Count==2,"default excludes logs");Check(zip.GetEntry("report.txt")!=null,"snapshot");using var r=new StreamReader(zip.GetEntry("report.txt").Open());Check(r.ReadToEnd().Contains("VR DLL SHA256: "),"identity hash");}
    using(var held=new FileStream(log,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite))
    {
        string next=SupportReportFiles.Write(root,"snapshot",dll,new[]{log,Path.Combine(root,"missing.log")},paths);
        Check(first!=next,"unique reports");using var zip=ZipFile.OpenRead(Path.Combine(root,next));Check(zip.Entries.Count==4,"optional tails only");
        using var reader=new StreamReader(zip.GetEntry("logs/test.log.txt").Open());string text=reader.ReadToEnd();Check(text.Contains("<local-path>")&&!text.Contains("TestPerson"),"zip redaction");
        using var missing=new StreamReader(zip.GetEntry("logs/missing.log.txt").Open());Check(missing.ReadToEnd().Contains("unavailable"),"missing log graceful");
    }
    using(var exclusive=new FileStream(log,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(SupportReportFiles.Tail(log).Contains("unavailable"),"locked log graceful");
    File.WriteAllText(log,new string('x',200000));Check(SupportReportFiles.Tail(log).Contains("No complete line"),"no unbounded long line");
    bool failed=false;try{SupportReportFiles.Write(log,"snapshot",dll,Array.Empty<string>(),paths);}catch(IOException){failed=true;}Check(failed,"unwritable target failure observable");
    Check(!Directory.EnumerateFiles(root,"*.partial").Any(),"no abandoned partials");
    Console.WriteLine($"PASS {checks} support report checks");
}
finally{Directory.Delete(root,true);}
