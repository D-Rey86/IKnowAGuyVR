using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace IKnowAGuyVR.SubsystemProbe;

internal static class VrControlReference
{
    internal static void Add(VrSettingsMenu.Page page)
    {
        var fold=new Foldout{name="IKAG_VR_ControlReference",text="Motion controller bindings",value=false};
        fold.style.color=new Color(1,.843f,.176f);fold.style.fontSize=page.Pause?18:15;
        fold.style.marginTop=16;fold.style.marginBottom=16;page.Content.Add(fold);
        Add(fold,page,()=>"Left primary = X on Touch / A on Index. Left secondary = Y on Touch / B on Index.");
        Add(fold,page,()=>"MOVE: "+VrControls.MoveStick+". TURN: "+VrControls.TurnStick+". SPRINT: Hold movement stick click. PAUSE: Double-click movement stick.");
        Add(fold,page,()=>"JUMP: "+VrControls.MainPrimary+". RECIPES: "+VrControls.OffPrimary+". CURSOR: "+VrControls.TurnStick+" click toggles the active hand pointer.");
        Add(fold,page,()=>"PICK UP: Grip. Tap grip to keep holding; hold grip and release to let go. USE HELD ITEM: "+VrControls.MainTrigger+". SECONDARY USE: "+VrControls.OffTrigger+". DROP: "+VrControls.OffSecondary+".");
        Add(fold,page,()=>"BUILD: Tap "+VrControls.MainSecondary+". SELL: Hold "+VrControls.MainSecondary+". While placing: that button confirms, "+VrControls.OffSecondary+" cancels, triggers rotate.");
        Add(fold,page,()=>"UI: Primary pointer on the "+(VrControls.LeftHanded?"left":"right")+" hand: trigger or grip. Other pointer: grip. BACK / EXIT MACHINE: "+VrControls.MainSecondary+".");
        Add(fold,page,()=>"WRIST: "+(VrControls.LeftHanded?"Right":"Left")+" wrist. Click section headings to collapse or expand. Use Map to open the map. Grab the pinned journal title to move it; while grabbed, the turning stick rotates it and adjusts its distance.");
        Add(fold,page,()=>"TOOLS: Swing the bat or your fist to strike. Grip is not required for a bat hit. CAMERA: "+VrControls.OffTrigger+" enters photo mode; "+VrControls.MainTrigger+" takes the photo.");
    }
    private static void Add(Foldout fold,VrSettingsMenu.Page page,Func<string> text)
    {
        var label=new Label(text());page.Updates.Add(()=>label.text=text());label.AddToClassList(page.Prefix+"setting-label");
        label.style.whiteSpace=WhiteSpace.Normal;label.style.marginTop=8;
        label.style.flexShrink=0;fold.Add(label);
    }
}
