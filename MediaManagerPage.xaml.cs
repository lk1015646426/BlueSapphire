using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using System.Xml.Linq;
using Windows.UI.Text;
using static System.Net.Mime.MediaTypeNames;

< ContentDialog
    x: Class = "BlueSapphire.DuplicateResultDialog"
    xmlns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns: x = "http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns: local = "using:BlueSapphire"
    xmlns: models = "using:BlueSapphire.Models"
    Title = "重复文件扫描结果"
    PrimaryButtonText = "删除选中项"
    CloseButtonText = "取消"
    DefaultButton = "Primary" >

    < ContentDialog.Resources >
        < DataTemplate x: Key = "DuplicateItemTemplate" x: DataType = "models:DuplicateItem" >
            < Grid >
                < Grid Visibility = "{x:Bind SeparatorVisibility}" Background = "{ThemeResource SystemControlBackgroundChromeMediumBrush}" Padding = "10,5" >
                    < TextBlock Text = "重复文件组" FontWeight = "Bold" Opacity = "0.8" />
                </ Grid >

                < Grid Visibility = "{x:Bind CheckBoxVisibility}" Padding = "10" Height = "60" >
                    < Grid.ColumnDefinitions >
                        < ColumnDefinition Width = "Auto" />
                        < ColumnDefinition Width = "60" />
                        < ColumnDefinition Width = "*" />
                    </ Grid.ColumnDefinitions >

                    < CheckBox IsChecked = "{x:Bind IsChecked, Mode=TwoWay}" VerticalAlignment = "Center" Margin = "0,0,10,0" />

                    < Border Grid.Column = "1" CornerRadius = "4" Background = "#10808080" Width = "50" Height = "50"  VerticalAlignment = "Center" >
                        < Image Source = "{x:Bind Thumbnail, Mode=OneWay}" Stretch = "UniformToFill" />
                    </ Border >

                    < StackPanel Grid.Column = "2" Margin = "10,0,0,0" VerticalAlignment = "Center" >
                        < TextBlock Text = "{x:Bind DisplayName}" TextTrimming = "CharacterEllipsis" />
                        < TextBlock Text = "{x:Bind DateString}" FontSize = "11" Opacity = "0.6" />
                        < TextBlock Text = "推荐保留" Visibility = "{x:Bind SuggestionVisibility}" Foreground = "Green" FontSize = "10" />
                    </ StackPanel >


                    < ToolTipService.ToolTip >
                        < ToolTip Content = "{x:Bind PathString}" />
                    </ ToolTipService.ToolTip >
                </ Grid >
            </ Grid >
        </ DataTemplate >
    </ ContentDialog.Resources >

    < ListView x: Name = "DuplicateList"
              SelectionMode = "None"
              MaxHeight = "500"
              Width = "450"
              ItemTemplate = "{StaticResource DuplicateItemTemplate}"
              ContainerContentChanging = "DuplicateList_ContainerContentChanging"
              DoubleTapped = "DuplicateList_DoubleTapped" />
</ ContentDialog >