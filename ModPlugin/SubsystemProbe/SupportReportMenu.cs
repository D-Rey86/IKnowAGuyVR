using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class SupportReportMenu
{
    internal static void Add(VrSettingsMenu.Page page)
    {
        var heading=new Label("SUPPORT");heading.AddToClassList(page.Prefix+"section-title");heading.style.color=new Color(1,.843f,.176f);heading.style.fontSize=page.Pause?18:13;page.Content.Add(heading);
        Label Text(string text){var label=new Label(text);label.AddToClassList(page.Prefix+"setting-label");label.style.whiteSpace=WhiteSpace.Normal;label.style.flexShrink=0;label.style.marginBottom=10;page.Content.Add(label);return label;}
        Text("Create a local troubleshooting ZIP with hardware, VR settings, resolution and current tracking status. No background recording or automatic upload.");
        var logs=new Toggle("Include recent logs (optional)"){name="IKAG_VR_ReportLogs",value=false};logs.style.color=new Color(1,1,1,.85f);logs.style.fontSize=page.Pause?18:15;logs.style.marginBottom=10;page.Content.Add(logs);
        Text("Logs may contain player names or network details. Known local paths are removed; review all files before sharing. Saves and screenshots are not collected.");
        var button=new Button{name="IKAG_VR_CreateReport",text="Create Diagnostic Report"};button.AddToClassList(page.Prefix+"tab");button.style.minHeight=42;button.style.flexShrink=0;page.Content.Add(button);
        var status=Text(SupportReport.Status);status.name="IKAG_VR_ReportStatus";
        // Reserve the saved-path message height before the first scroll/click.
        status.style.minHeight=page.Pause?84:75;
        IVisualElementScheduledItem poll=null;
        void Create()
        {
            SupportReport.Create(logs.value);logs.SetValueWithoutNotify(false);page.Refresh();
            if(!SupportReport.Busy)return;
            if(poll==null)poll=status.schedule.Execute((Action)(()=>{page.Refresh();if(!SupportReport.Busy)poll.Pause();})).Every(100);
            else poll.Resume();
        }
        button.RegisterCallback<ClickEvent>((Action<ClickEvent>)(_=>Create()));
        button.RegisterCallback<NavigationSubmitEvent>((Action<NavigationSubmitEvent>)(e=>{Create();e.StopImmediatePropagation();}));
        page.Updates.Add(()=>{button.SetEnabled(!SupportReport.Busy);logs.SetEnabled(!SupportReport.Busy);status.text=SupportReport.Status;});
    }
}
