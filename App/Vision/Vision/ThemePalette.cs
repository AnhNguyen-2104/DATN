using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
namespace PlateVision50.Vision;
public static class ThemePalette {
    private static readonly Dictionary<string,(string Light,string Dark)> Colors=new() {
        ["VDTheme001"]=("#E8EBEF","#222D3B"),
        ["VDTheme002"]=("#23313E","#DCE6EF"),
        ["VDTheme003"]=("#2E6E95","#223144"),
        ["VDTheme004"]=("#FFFFFF","#EAF2F8"),
        ["VDTheme005"]=("#AEB9C3","#46586A"),
        ["VDTheme006"]=("#293845","#203246"),
        ["VDTheme007"]=("#253544","#203246"),
        ["VDTheme008"]=("#ABB7C3","#46586A"),
        ["VDTheme009"]=("#FFFFFF","#F3F6F8"),
        ["VDTheme010"]=("#263440","#203246"),
        ["VDTheme011"]=("#3C5365","#DCE6EF"),
        ["VDTheme012"]=("#63727E","#DCE6EF"),
        ["VDTheme013"]=("#253544","#DCE6EF"),
        ["VDTheme014"]=("#647381","#DCE6EF"),
        ["VDTheme015"]=("#47687D","#DCE6EF"),
        ["VDTheme016"]=("#985F29","#DCE6EF"),
        ["VDTheme017"]=("#536A7C","#DCE6EF"),
        ["VDTheme018"]=("#E7EBEF","#222D3B"),
        ["VDTheme019"]=("#445563","#DCE6EF"),
        ["VDTheme020"]=("#EDF1F4","#222D3B"),
        ["VDTheme021"]=("#CBD3DA","#46586A"),
        ["VDTheme022"]=("#D5E3EC","#222D3B"),
        ["VDTheme023"]=("#1E4E6B","#DCE6EF"),
        ["VDTheme024"]=("#27698F","#335F83"),
        ["VDTheme025"]=("#FFFFFF","#F6F9FC"),
        ["VDTheme026"]=("#367760","#316854"),
        ["VDTheme027"]=("#727D85","#335F83"),
        ["VDTheme028"]=("#E3E8ED","#222D3B"),
        ["VDTheme029"]=("#263743","#ECF2F9"),
        ["VDTheme030"]=("#7F602E","#DCE6EF"),
        ["VDTheme031"]=("#687780","#384859"),
        ["VDTheme032"]=("#DD101E31","#223144"),
        ["VDTheme033"]=("#D8E6EF","#EAF2F8"),
        ["VDTheme034"]=("#E1E6EB","#EAF2F8"),
        ["VDTheme035"]=("#DA101E31","#223144"),
        ["VDTheme036"]=("#E0B465","#B5CBDC"),
        ["VDTheme037"]=("#E5E9EF","#EAF2F8"),
        ["VDTheme038"]=("#2D3E49","#DCE6EF"),
        ["VDTheme039"]=("#DFE5EA","#222D3B"),
        ["VDTheme040"]=("#2E3E49","#DCE6EF"),
        ["VDTheme041"]=("#C0CAD2","#46586A"),
        ["VDTheme042"]=("#677987","#DCE6EF"),
        ["VDTheme043"]=("#315D76","#DCE6EF"),
        ["VDTheme044"]=("#586D7C","#DCE6EF"),
        ["VDTheme045"]=("#5B7082","#DCE6EF"),
        ["VDTheme046"]=("#243F52","#DCE6EF"),
        ["VDTheme047"]=("#786539","#DCE6EF"),
        ["VDTheme048"]=("#657785","#DCE6EF"),
        ["VDTheme049"]=("#8998A3","#B5CBDC"),
        ["VDTheme050"]=("#647889","#DCE6EF"),
        ["VDTheme051"]=("#2B5B5A","#DCE6EF"),
        ["VDTheme052"]=("#466776","#DCE6EF"),
        ["VDTheme053"]=("#6C7D8A","#DCE6EF"),
        ["VDTheme054"]=("#304454","#DCE6EF"),
        ["VDTheme055"]=("#70808C","#DCE6EF"),
        ["VDTheme056"]=("#25333E","#DCE6EF"),
        ["VDTheme057"]=("#607587","#DCE6EF"),
        ["VDTheme058"]=("#627483","#DCE6EF"),
        ["VDTheme059"]=("#536D80","#DCE6EF"),
        ["VDTheme060"]=("#347386","#335F83"),
        ["VDTheme061"]=("#5E7788","#DCE6EF"),
        ["VDTheme062"]=("#9C6D33","#DCE6EF"),
        ["VDTheme063"]=("#657887","#DCE6EF"),
        ["VDTheme064"]=("#98662F","#DCE6EF"),
        ["VDTheme065"]=("#577181","#DCE6EF"),
        ["VDTheme066"]=("#9B682B","#DCE6EF"),
        ["VDTheme067"]=("#314858","#DCE6EF"),
        ["VDTheme068"]=("#9C6B2D","#DCE6EF"),
        ["VDTheme069"]=("#866746","#223144"),
        ["VDTheme070"]=("#667683","#DCE6EF"),
        ["VDTheme071"]=("#59778A","#DCE6EF"),
        ["VDTheme072"]=("#354B5D","#DCE6EF"),
        ["VDTheme073"]=("#63727E","#DCE6EF"),
        ["VDTheme074"]=("#986832","#DCE6EF"),
        ["VDTheme075"]=("#70808D","#DCE6EF"),
        ["VDTheme076"]=("#95652D","#DCE6EF"),
        ["VDTheme077"]=("#283743","#DCE6EF"),
        ["VDTheme078"]=("#94652D","#DCE6EF"),
        ["VDTheme079"]=("#DCE2E7","#222D3B"),
        ["VDTheme080"]=("#C4CDD6","#46586A"),
        ["VDTheme081"]=("#304A5F","#DCE6EF"),
        ["VDThemeComboBg"]=("#FFFFFF","#F3F6F8"),
        ["VDThemeComboFg"]=("#263440","#203246")
    };
    public static void Set(Window window,bool dark) {
        foreach(var (key,pair) in Colors) {
            if(window.TryFindResource(key) is SolidColorBrush b) {
                var color=(Color)ColorConverter.ConvertFromString(dark?pair.Dark:pair.Light);
                if(b.IsFrozen) window.Resources[key]=new SolidColorBrush(color);
                else b.Color=color;
            }
        }
        void Change(string k,string light,string night) {
            if(window.TryFindResource(k) is SolidColorBrush b) {
                var color=(Color)ColorConverter.ConvertFromString(dark?night:light);
                if(b.IsFrozen)window.Resources[k]=new SolidColorBrush(color);
                else b.Color=color;
            }
        }
        Change("Panel","#F7F8FA","#1B2634");
        Change("Field","#FFFFFF","#233143");
        Change("Stroke","#C5CED7","#465768");
        window.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#111923":"#E8EBEF"));
        window.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#E9F1F8":"#23313E"));
        // Important: WPF DataGrid empty viewport can retain system-white RowBackground
        // even after changing a DynamicResource brush; force foreground, background
        // and row surfaces for BOTH monitor and log grids after each theme switch.
        foreach(var name in new[]{"PipelineTable","PipelineHistoryTable"})
        {
            if(window.FindName(name) is System.Windows.Controls.DataGrid grid)
            {
                Color Parse(string value)=>(Color)ColorConverter.ConvertFromString(value);
                grid.Background=new SolidColorBrush(Parse(dark?"#1B2837":"#FFFFFF"));
                grid.RowBackground=new SolidColorBrush(Parse(dark?"#1B2837":"#FFFFFF"));
                grid.AlternatingRowBackground=new SolidColorBrush(Parse(dark?"#233346":"#F2F5F7"));
                grid.Foreground=new SolidColorBrush(Parse(dark?"#E6EFF7":"#25333E"));
                grid.HorizontalGridLinesBrush=new SolidColorBrush(Parse(dark?"#435468":"#D7DFE5"));
            }
        }
    }
}
