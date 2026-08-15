import shutil
from pathlib import Path

root = Path(__file__).parent

# Backup originals
for name in ['ProbeControlWindow.xaml', 'DataAnalysisWindow.xaml', 'SplashWindow.xaml']:
    src = root / name
    if src.exists():
        shutil.copy(src, root / (name + '.bak'))

probe_xaml = r'''<Window x:Class="UpperMachine.ProbeControlWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        Title="探针控制"
        Width="1180"
        Height="760"
        MinWidth="1040"
        MinHeight="680"
        WindowStartupLocation="CenterOwner"
        Background="#0B1117"
        FontFamily="Microsoft YaHei UI">
    <Window.Resources>
        <SolidColorBrush x:Key="PanelBrush" Color="#18212B" />
        <SolidColorBrush x:Key="PanelSoftBrush" Color="#202E3B" />
        <SolidColorBrush x:Key="BorderBrushStrong" Color="#2E4253" />
        <SolidColorBrush x:Key="TextBrush" Color="#EAF1F7" />
        <SolidColorBrush x:Key="MutedTextBrush" Color="#C2D0DC" />
        <SolidColorBrush x:Key="AccentBrush" Color="#58C7A3" />
        <SolidColorBrush x:Key="AccentTextBrush" Color="#071412" />
        <SolidColorBrush x:Key="InputBrush" Color="#111922" />
        <SolidColorBrush x:Key="InputBorderBrush" Color="#314353" />
        <SolidColorBrush x:Key="DangerBrush" Color="#D96B76" />

        <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
        </Style>
        <Style TargetType="Label">
            <Setter Property="Foreground" Value="{StaticResource MutedTextBrush}" />
            <Setter Property="Margin" Value="0,0,0,4" />
        </Style>
        <Style TargetType="TextBox">
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
            <Setter Property="Background" Value="{StaticResource InputBrush}" />
            <Setter Property="BorderBrush" Value="{StaticResource InputBorderBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="10,7" />
            <Setter Property="FontSize" Value="13" />
            <Setter Property="Margin" Value="0,0,0,12" />
        </Style>

        <Style x:Key="CardBorderStyle" TargetType="Border">
            <Setter Property="Background" Value="{StaticResource PanelBrush}" />
            <Setter Property="BorderBrush" Value="{StaticResource BorderBrushStrong}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="8" />
            <Setter Property="Padding" Value="18" />
            <Setter Property="Margin" Value="0,0,0,16" />
        </Style>
        <Style x:Key="SectionTitleStyle" TargetType="TextBlock">
            <Setter Property="FontFamily" Value="Bahnschrift SemiBold" />
            <Setter Property="FontSize" Value="16" />
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
            <Setter Property="Margin" Value="0,0,0,14" />
        </Style>

        <Style x:Key="BaseButtonStyle" TargetType="Button">
            <Setter Property="Height" Value="40" />
            <Setter Property="Padding" Value="18,0" />
            <Setter Property="FontFamily" Value="Bahnschrift SemiBold" />
            <Setter Property="FontSize" Value="13" />
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
            <Setter Property="Background" Value="{StaticResource PanelSoftBrush}" />
            <Setter Property="BorderBrush" Value="{StaticResource BorderBrushStrong}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="RootBorder"
                                Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                CornerRadius="6">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="RootBorder" Property="Background" Value="#2A3C4D" />
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="RootBorder" Property="Background" Value="#1F2E3B" />
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter TargetName="RootBorder" Property="Opacity" Value="0.5" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style x:Key="ActionButtonStyle" TargetType="Button" BasedOn="{StaticResource BaseButtonStyle}">
            <Setter Property="Background" Value="{StaticResource PanelSoftBrush}" />
        </Style>
        <Style x:Key="AccentButtonStyle" TargetType="Button" BasedOn="{StaticResource BaseButtonStyle}">
            <Setter Property="Background" Value="{StaticResource AccentBrush}" />
            <Setter Property="Foreground" Value="{StaticResource AccentTextBrush}" />
            <Setter Property="BorderBrush" Value="#7DE4C2" />
        </Style>
        <Style x:Key="DangerButtonStyle" TargetType="Button" BasedOn="{StaticResource BaseButtonStyle}">
            <Setter Property="Background" Value="#3A2328" />
            <Setter Property="Foreground" Value="{StaticResource DangerBrush}" />
            <Setter Property="BorderBrush" Value="#7A3A42" />
        </Style>
        <Style x:Key="JogButtonStyle" TargetType="Button" BasedOn="{StaticResource BaseButtonStyle}">
            <Setter Property="Width" Value="72" />
            <Setter Property="Height" Value="56" />
            <Setter Property="FontSize" Value="18" />
            <Setter Property="FontFamily" Value="Bahnschrift Bold" />
        </Style>
    </Window.Resources>

    <Grid Margin="18">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="18" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Padding="18"
                Background="{StaticResource PanelBrush}"
                BorderBrush="{StaticResource BorderBrushStrong}"
                BorderThickness="1"
                CornerRadius="8">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel>
                    <TextBlock FontFamily="Bahnschrift SemiBold"
                               FontSize="24"
                               Text="探针控制" />
                    <TextBlock Margin="0,8,0,0"
                               Foreground="{StaticResource MutedTextBrush}"
                               Text="手动控制探针移动、抬笔落笔与常用指令发送" />
                </StackPanel>
                <StackPanel Grid.Column="1"
                            Orientation="Horizontal">
                    <Button Style="{StaticResource ActionButtonStyle}"
                            Click="SaveSettingsButton_Click"
                            Content="保存指令"
                            Margin="0,0,10,0" />
                    <Button Style="{StaticResource AccentButtonStyle}"
                            Click="CloseWindowButton_Click"
                            Content="关闭" />
                </StackPanel>
            </Grid>
        </Border>

        <Grid Grid.Row="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="380" />
                <ColumnDefinition Width="18" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel>
                    <Border Style="{StaticResource CardBorderStyle}">
                        <StackPanel>
                            <TextBlock Style="{StaticResource SectionTitleStyle}" Text="探针点动" />
                            <Grid Margin="0,0,0,14">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="10" />
                                    <ColumnDefinition Width="*" />
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <Label Content="点动步长 (mm)" />
                                    <TextBox x:Name="StepTextBox" Text="1" />
                                </StackPanel>
                                <StackPanel Grid.Column="2">
                                    <Label Content="点动速度 (mm/min)" />
                                    <TextBox x:Name="FeedTextBox" Text="500" />
                                </StackPanel>
                            </Grid>

                            <Grid HorizontalAlignment="Center" Margin="0,4,0,0">
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                </Grid.RowDefinitions>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <Button Grid.Row="0" Grid.Column="1"
                                        Style="{StaticResource JogButtonStyle}"
                                        Click="MoveUpButton_Click"
                                        Content="Y+"
                                        Margin="6" />
                                <Button Grid.Row="1" Grid.Column="0"
                                        Style="{StaticResource JogButtonStyle}"
                                        Click="MoveLeftButton_Click"
                                        Content="X-"
                                        Margin="6" />
                                <Border Grid.Row="1" Grid.Column="1"
                                        Width="72"
                                        Height="56"
                                        Margin="6"
                                        Background="{StaticResource InputBrush}"
                                        BorderBrush="{StaticResource InputBorderBrush}"
                                        BorderThickness="1"
                                        CornerRadius="6">
                                    <TextBlock HorizontalAlignment="Center"
                                               VerticalAlignment="Center"
                                               Foreground="{StaticResource MutedTextBrush}"
                                               FontSize="12"
                                               Text="原点" />
                                </Border>
                                <Button Grid.Row="1" Grid.Column="2"
                                        Style="{StaticResource JogButtonStyle}"
                                        Click="MoveRightButton_Click"
                                        Content="X+"
                                        Margin="6" />
                                <Button Grid.Row="2" Grid.Column="1"
                                        Style="{StaticResource JogButtonStyle}"
                                        Click="MoveDownButton_Click"
                                        Content="Y-"
                                        Margin="6" />
                            </Grid>
                        </StackPanel>
                    </Border>

                    <Border Style="{StaticResource CardBorderStyle}">
                        <StackPanel>
                            <TextBlock Style="{StaticResource SectionTitleStyle}" Text="抬笔落笔" />
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="10" />
                                    <ColumnDefinition Width="*" />
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <Label Content="抬笔命令" />
                                    <TextBox x:Name="RaiseCommandTextBox" />
                                </StackPanel>
                                <StackPanel Grid.Column="2">
                                    <Label Content="落笔命令" />
                                    <TextBox x:Name="DropCommandTextBox" />
                                </StackPanel>
                            </Grid>
                            <Label Content="保持命令" />
                            <TextBox x:Name="HoldCommandTextBox" />
                            <WrapPanel Margin="0,8,0,0">
                                <Button Style="{StaticResource AccentButtonStyle}"
                                        Click="VerifyRaiseButton_Click"
                                        Content="验证抬笔"
                                        Margin="0,0,10,8" />
                                <Button Style="{StaticResource AccentButtonStyle}"
                                        Click="VerifyDropButton_Click"
                                        Content="验证落笔"
                                        Margin="0,0,10,8" />
                                <Button Style="{StaticResource ActionButtonStyle}"
                                        Click="SendHoldButton_Click"
                                        Content="发送保持"
                                        Margin="0,0,0,8" />
                            </WrapPanel>
                        </StackPanel>
                    </Border>
                </StackPanel>
            </ScrollViewer>

            <Border Grid.Column="2"
                    Padding="18"
                    Background="{StaticResource PanelBrush}"
                    BorderBrush="{StaticResource BorderBrushStrong}"
                    BorderThickness="1"
                    CornerRadius="8">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>
                    <TextBlock Style="{StaticResource SectionTitleStyle}" Text="串口日志" />
                    <TextBox x:Name="LogTextBox"
                             Grid.Row="1"
                             Background="#101720"
                             BorderBrush="#101720"
                             FontFamily="Consolas"
                             FontSize="12"
                             AcceptsReturn="True"
                             IsReadOnly="True"
                             TextWrapping="Wrap"
                             VerticalScrollBarVisibility="Auto" />
                </Grid>
            </Border>
        </Grid>
    </Grid>
</Window>
'''

data_analysis_xaml = r'''<Window x:Class="UpperMachine.DataAnalysisWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        Title="AI 数据分析"
        Width="1320"
        Height="860"
        MinWidth="1080"
        MinHeight="720"
        WindowStartupLocation="CenterOwner"
        Background="#0B1117"
        FontFamily="Microsoft YaHei UI">
    <Window.Resources>
        <SolidColorBrush x:Key="PanelBrush" Color="#18212B" />
        <SolidColorBrush x:Key="PanelSoftBrush" Color="#202E3B" />
        <SolidColorBrush x:Key="BorderBrushStrong" Color="#2E4253" />
        <SolidColorBrush x:Key="TextBrush" Color="#EAF1F7" />
        <SolidColorBrush x:Key="MutedTextBrush" Color="#C2D0DC" />
        <SolidColorBrush x:Key="AccentBrush" Color="#58C7A3" />
        <SolidColorBrush x:Key="AccentTextBrush" Color="#071412" />
        <SolidColorBrush x:Key="InputBrush" Color="#111922" />
        <SolidColorBrush x:Key="InputBorderBrush" Color="#314353" />
        <SolidColorBrush x:Key="HighlightBrush" Color="#F0BA67" />

        <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
        </Style>
        <Style TargetType="TextBox">
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
            <Setter Property="Background" Value="{StaticResource InputBrush}" />
            <Setter Property="BorderBrush" Value="{StaticResource InputBorderBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="10,7" />
            <Setter Property="FontSize" Value="13" />
        </Style>
        <Style x:Key="BaseButtonStyle" TargetType="Button">
            <Setter Property="Height" Value="40" />
            <Setter Property="Padding" Value="18,0" />
            <Setter Property="FontFamily" Value="Bahnschrift SemiBold" />
            <Setter Property="FontSize" Value="13" />
            <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
            <Setter Property="Background" Value="{StaticResource PanelSoftBrush}" />
            <Setter Property="BorderBrush" Value="{StaticResource BorderBrushStrong}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="RootBorder"
                                Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                CornerRadius="6">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="RootBorder" Property="Background" Value="#2A3C4D" />
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="RootBorder" Property="Background" Value="#1F2E3B" />
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter TargetName="RootBorder" Property="Opacity" Value="0.5" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style x:Key="AccentButtonStyle" TargetType="Button" BasedOn="{StaticResource BaseButtonStyle}">
            <Setter Property="Background" Value="{StaticResource AccentBrush}" />
            <Setter Property="Foreground" Value="{StaticResource AccentTextBrush}" />
            <Setter Property="BorderBrush" Value="#7DE4C2" />
        </Style>
        <Style x:Key="HighlightButtonStyle" TargetType="Button" BasedOn="{StaticResource BaseButtonStyle}">
            <Setter Property="Background" Value="#3A3220" />
            <Setter Property="Foreground" Value="{StaticResource HighlightBrush}" />
            <Setter Property="BorderBrush" Value="#8A7038" />
        </Style>
    </Window.Resources>

    <Grid Margin="18">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="18" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Padding="18"
                Background="{StaticResource PanelBrush}"
                BorderBrush="{StaticResource BorderBrushStrong}"
                BorderThickness="1"
                CornerRadius="8">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel>
                    <TextBlock FontFamily="Bahnschrift SemiBold"
                               FontSize="24"
                               Text="AI 数据分析" />
                    <TextBlock x:Name="SummaryTextBlock"
                               Margin="0,10,0,0"
                               Foreground="{StaticResource MutedTextBrush}"
                               TextWrapping="Wrap" />
                </StackPanel>
                <StackPanel Grid.Column="1" HorizontalAlignment="Right">
                    <TextBlock Foreground="{StaticResource MutedTextBrush}" Text="数据量" />
                    <TextBlock x:Name="DatasetInfoTextBlock"
                               Margin="0,6,0,0"
                               FontFamily="Bahnschrift SemiBold"
                               FontSize="16"
                               Text="0 条" />
                </StackPanel>
            </Grid>
        </Border>

        <Grid Grid.Row="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="420" />
                <ColumnDefinition Width="18" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <Border Padding="18"
                    Background="{StaticResource PanelBrush}"
                    BorderBrush="{StaticResource BorderBrushStrong}"
                    BorderThickness="1"
                    CornerRadius="8">
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel>
                        <TextBlock FontFamily="Bahnschrift SemiBold"
                                   FontSize="18"
                                   Margin="0,0,0,14"
                                   Text="设置" />

                        <TextBlock Foreground="{StaticResource MutedTextBrush}" Text="请求 URL" />
                        <TextBox x:Name="UrlTextBox"
                                 Margin="0,6,0,14"
                                 Text="https://api.deepseek.com/responses" />

                        <TextBlock Foreground="{StaticResource MutedTextBrush}" Text="API Key" />
                        <PasswordBox x:Name="ApiKeyPasswordBox"
                                     Margin="0,6,0,14"
                                     Height="38"
                                     Background="{StaticResource InputBrush}"
                                     BorderBrush="{StaticResource InputBorderBrush}"
                                     Foreground="{StaticResource TextBrush}"
                                     Padding="10,0" />

                        <TextBlock Foreground="{StaticResource MutedTextBrush}" Text="模型" />
                        <TextBox x:Name="ModelTextBox"
                                 Margin="0,6,0,14"
                                 Text="deepseek-v4-flash" />

                        <TextBlock Foreground="{StaticResource MutedTextBrush}" Text="补充要求" />
                        <TextBox x:Name="PromptTextBox"
                                 Margin="0,6,0,14"
                                 AcceptsReturn="True"
                                 TextWrapping="Wrap"
                                 VerticalScrollBarVisibility="Auto"
                                 MinHeight="180"
                                 Text="请分析这组扫描数据，重点给出整体结论、异常点、趋势特征和下一步建议。" />

                        <TextBlock Foreground="{StaticResource MutedTextBrush}" Text="说明" />
                        <TextBlock Margin="0,6,0,14"
                                   Foreground="{StaticResource MutedTextBrush}"
                                   TextWrapping="Wrap"
                                   Text="URL 可指向任意兼容 OpenAI 的接口（如 DeepSeek、通义、智谱、Ollama），支持 /responses 与 /chat/completions 两种格式，按地址自动识别。API Key 留空时将读取 OPENAI_API_KEY 环境变量。" />

                        <StackPanel Orientation="Horizontal">
                            <Button x:Name="AnalyzeButton"
                                    Style="{StaticResource AccentButtonStyle}"
                                    Click="AnalyzeButton_Click"
                                    Content="开始分析"
                                    Margin="0,0,10,0" />
                            <Button Style="{StaticResource HighlightButtonStyle}"
                                    Click="CopyResultButton_Click"
                                    Content="复制结果"
                                    Margin="0,0,10,0" />
                            <Button Style="{StaticResource BaseButtonStyle}"
                                    Click="CloseButton_Click"
                                    Content="关闭" />
                        </StackPanel>

                        <TextBlock x:Name="StatusTextBlock"
                                   Margin="0,14,0,0"
                                   Foreground="{StaticResource MutedTextBrush}"
                                   Text="等待输入" />
                    </StackPanel>
                </ScrollViewer>
            </Border>

            <Border Grid.Column="2"
                    Padding="18"
                    Background="{StaticResource PanelBrush}"
                    BorderBrush="{StaticResource BorderBrushStrong}"
                    BorderThickness="1"
                    CornerRadius="8">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>

                    <TextBlock FontFamily="Bahnschrift SemiBold"
                               FontSize="18"
                               Margin="0,0,0,14"
                               Text="结果" />

                    <TextBox x:Name="ResultTextBox"
                             Grid.Row="1"
                             Margin="0"
                             Background="#101720"
                             BorderBrush="#101720"
                             Foreground="{StaticResource TextBrush}"
                             FontFamily="Consolas"
                             AcceptsReturn="True"
                             IsReadOnly="True"
                             TextWrapping="Wrap"
                             VerticalScrollBarVisibility="Auto" />
                </Grid>
            </Border>
        </Grid>
    </Grid>
</Window>
'''

splash_xaml = r'''<Window x:Class="UpperMachine.SplashWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="启动中"
        Width="760"
        Height="400"
        WindowStartupLocation="CenterScreen"
        WindowStyle="None"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True">
    <Border Margin="12"
            Padding="24"
            Background="#0B1117"
            BorderBrush="#2E6C63"
            BorderThickness="1.2"
            CornerRadius="24">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <Border Background="#111922"
                    BorderBrush="#2C3E4D"
                    BorderThickness="1"
                    CornerRadius="18">
                <Image Source="Assets/splash-logo.png"
                       Stretch="Uniform"
                       Margin="10" />
            </Border>

            <StackPanel Grid.Row="1" Margin="0,18,0,0">
                <ProgressBar Height="8"
                             IsIndeterminate="True"
                             Foreground="#5ADAB1"
                             Background="#1A2530"
                             BorderThickness="0" />
                <TextBlock Margin="0,12,0,0"
                           HorizontalAlignment="Center"
                           Foreground="#C2D0DC"
                           FontSize="14"
                           Text="正在加载界面与设备控制模块..." />
            </StackPanel>
        </Grid>
    </Border>
</Window>
'''

(root / 'ProbeControlWindow.xaml').write_text(probe_xaml, encoding='utf-8')
(root / 'DataAnalysisWindow.xaml').write_text(data_analysis_xaml, encoding='utf-8')
(root / 'SplashWindow.xaml').write_text(splash_xaml, encoding='utf-8')

print('UI rewrite done.')
