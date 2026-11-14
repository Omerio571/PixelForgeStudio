using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PixelForge.Desktop;

// A presentation wrapper around WPF Slider. Value remains the rendering parameter.
public sealed class NumericSlider:UserControl
{
    public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(double),typeof(NumericSlider),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,Changed));
    public static readonly DependencyProperty MinimumProperty=DependencyProperty.Register(nameof(Minimum),typeof(double),typeof(NumericSlider),new PropertyMetadata(0d,Changed));
    public static readonly DependencyProperty MaximumProperty=DependencyProperty.Register(nameof(Maximum),typeof(double),typeof(NumericSlider),new PropertyMetadata(1d,Changed));
    public static readonly DependencyProperty DisplayScaleProperty=DependencyProperty.Register(nameof(DisplayScale),typeof(double),typeof(NumericSlider),new PropertyMetadata(1d,Changed));
    public static readonly DependencyProperty UnitProperty=DependencyProperty.Register(nameof(Unit),typeof(string),typeof(NumericSlider),new PropertyMetadata("",Changed));
    public static readonly DependencyProperty NumericModeProperty=DependencyProperty.Register(nameof(NumericMode),typeof(bool),typeof(NumericSlider),new PropertyMetadata(false,Changed));
    public static readonly DependencyProperty DecimalPlacesProperty=DependencyProperty.Register(nameof(DecimalPlaces),typeof(int),typeof(NumericSlider),new PropertyMetadata(2,Changed),v=>(int)v is >=0 and <=2);
    public double Value{get=>(double)GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
    public double Minimum{get=>(double)GetValue(MinimumProperty);set=>SetValue(MinimumProperty,value);}
    public double Maximum{get=>(double)GetValue(MaximumProperty);set=>SetValue(MaximumProperty,value);}
    public double DisplayScale{get=>(double)GetValue(DisplayScaleProperty);set=>SetValue(DisplayScaleProperty,value);}
    public string Unit{get=>(string)GetValue(UnitProperty);set=>SetValue(UnitProperty,value);}
    public bool NumericMode{get=>(bool)GetValue(NumericModeProperty);set=>SetValue(NumericModeProperty,value);}
    public int DecimalPlaces{get=>(int)GetValue(DecimalPlacesProperty);set=>SetValue(DecimalPlacesProperty,value);}
    public double TickFrequency{get=>slider.TickFrequency;set=>slider.TickFrequency=value;}
    public event RoutedPropertyChangedEventHandler<double>? ValueChanged;
    public event EventHandler? ModeChanged;
    public Slider Slider=>slider;
    public TextBox Editor=>editor;
    public string ValidationMessage=>error.Text;
    private readonly Slider slider=new(){VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBox editor=new(){MinWidth=65,VerticalContentAlignment=VerticalAlignment.Center};
    private readonly TextBlock unit=new(){VerticalAlignment=VerticalAlignment.Center,Margin=new(5,0,0,0)};
    private readonly TextBlock error=new(){FontSize=11,TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed,Margin=new(0,2,0,0)};
    private readonly Button toggle=new(){Width=30,Height=28,Padding=new(5),Margin=new(5,0,0,0),VerticalAlignment=VerticalAlignment.Center};
    private bool updating,dirty,ready;
    public NumericSlider()
    {
        var root=new Grid();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        row.Children.Add(slider);row.Children.Add(editor);Grid.SetColumn(unit,1);row.Children.Add(unit);Grid.SetColumn(toggle,2);row.Children.Add(toggle);
        root.Children.Add(row);Grid.SetRow(error,1);root.Children.Add(error);Content=root;
        slider.ValueChanged+=(_,e)=>{if(!updating)SetCurrentValue(ValueProperty,e.NewValue);};
        editor.TextChanged+=(_,_)=>{if(!updating)dirty=true;};
        editor.LostKeyboardFocus+=(_,_)=>Commit();
        editor.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit();e.Handled=true;}else if(e.Key==Key.Escape){CancelEdit();e.Handled=true;}};
        toggle.Click+=(_,_)=>{if(!Commit())return;NumericMode=!NumericMode;ModeChanged?.Invoke(this,EventArgs.Empty);if(NumericMode){editor.Focus();editor.SelectAll();}};
        error.SetResourceReference(TextBlock.ForegroundProperty,"Error");ready=true;Refresh();
    }
    private static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        var control=(NumericSlider)d;if(!control.ready)return;control.Refresh();
        if(e.Property==ValueProperty)control.ValueChanged?.Invoke(control,new((double)e.OldValue,(double)e.NewValue));
    }
    private void Refresh()
    {
        updating=true;
        slider.Minimum=Minimum;slider.Maximum=Maximum;slider.Value=Value;
        editor.Text=UiNumbers.Format(Value*DisplayScale,DecimalPlaces);dirty=false;
        slider.Visibility=NumericMode?Visibility.Collapsed:Visibility.Visible;editor.Visibility=NumericMode?Visibility.Visible:Visibility.Collapsed;
        unit.Text=Unit;unit.Visibility=NumericMode&&!string.IsNullOrEmpty(Unit)?Visibility.Visible:Visibility.Collapsed;
        toggle.ToolTip=NumericMode?"Switch to slider":"Switch to numeric input";
        System.Windows.Automation.AutomationProperties.SetName(toggle,toggle.ToolTip.ToString());
        var icon=new Path{Data=Geometry.Parse(NumericMode?"M3,2 L3,16 M9,2 L9,16 M15,2 L15,16 M0,6 L6,6 M6,12 L12,12 M12,5 L18,5":"M1,3 L19,3 L19,15 L1,15 Z M4,6 L6,6 M9,6 L11,6 M14,6 L16,6 M4,9 L6,9 M9,9 L11,9 M14,9 L16,9 M5,12 L15,12"),StrokeThickness=1.3,Stretch=Stretch.Uniform};
        icon.SetResourceReference(Shape.StrokeProperty,"Text");toggle.Content=icon;
        error.Text="";error.Visibility=Visibility.Collapsed;editor.ClearValue(Border.BorderBrushProperty);updating=false;
    }
    public bool Commit()
    {
        if(!dirty)return string.IsNullOrEmpty(error.Text);
        if(!UiNumbers.TryParse(editor.Text,DecimalPlaces,out double displayed)||displayed<Minimum*DisplayScale||displayed>Maximum*DisplayScale)
        {
            error.Text=$"Enter {UiNumbers.Format(Minimum*DisplayScale)}–{UiNumbers.Format(Maximum*DisplayScale)}{Unit}; "+(DecimalPlaces==0?"whole numbers only.":"up to 2 decimal places.");error.Visibility=Visibility.Visible;editor.BorderBrush=error.Foreground;return false;
        }
        double value=displayed/DisplayScale;SetCurrentValue(ValueProperty,value);Refresh();return true;
    }
    public void CancelEdit()=>Refresh();
}
