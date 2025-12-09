; *** Inno Setup version 6.0.0+ Chinese Simplified messages ***
;
[LangOptions]
LanguageName=Chinese Simplified
LanguageID=$0804
LanguageCodePage=936
DialogFontName=Microsoft YaHei UI
DialogFontSize=9
WelcomeFontName=Microsoft YaHei UI
WelcomeFontSize=12
TitleFontName=Microsoft YaHei UI
TitleFontSize=29
CopyrightFontName=Microsoft YaHei UI
CopyrightFontSize=9

[Messages]
SetupAppTitle=安装程序
SetupWindowTitle=安装 - %1
ExitSetupTitle=退出安装程序
ExitSetupMessage=安装尚未完成。如果您现在退出，程序将不会被安装。%n%n您可以在稍后的时候运行安装程序来完成安装。%n%n退出安装程序吗?
ButtonSetup=&安装
ButtonMessages=&消息
ButtonClose=&关闭
ButtonYes=&是
ButtonNo=&否
ButtonOK=确定
ButtonCancel=取消
ButtonAbort=&中止
ButtonRetry=&重试
ButtonIgnore=&忽略
ButtonBrowse=&浏览...
ButtonWizardBrowse=浏览(&B)...
ButtonNewFolder=&新建文件夹

; *** Wizard common messages
ClickNext=单击“下一步”继续，或单击“取消”退出安装程序。
BeveledLabel=
BrowseDialogTitle=浏览文件夹
BrowseDialogLabel=在下面的列表中选择一个文件夹，然后单击“确定”。
NewFolderName=新文件夹

; *** Wizard pages
WizardWelcome=欢迎使用 [name] 安装向导
WelcomeLabel1=安装向导将把 [name] 安装到您的计算机中。
WelcomeLabel2=建议您在继续之前关闭所有其它应用程序。

WizardSelectDir=选择目标位置
SelectDirDesc=您想将 [name] 安装在哪里?
SelectDirLabel3=安装程序将把 [name] 安装到以下文件夹中。
SelectDirBrowseLabel=若要继续，请单击“下一步”。如果您想选择其它文件夹，请单击“浏览”。
DiskSpaceMBLabel=至少需要有 [mb] MB 的可用磁盘空间。

WizardSelectTasks=选择附加任务
SelectTasksDesc=您想让安装程序执行哪些附加任务?
SelectTasksLabel2=选择您想让安装程序在安装 [name] 时执行的附加任务，然后单击“下一步”。

WizardReady=准备安装
ReadyLabel1=安装程序现在准备开始将 [name] 安装到您的计算机中。
ReadyLabel2a=单击“安装”继续此安装程序。如果您想回顾或修改设置，请单击“上一步”。
ReadyLabel2b=单击“安装”继续此安装程序。
ReadyMemoUserInfo=用户信息:
ReadyMemoDir=目标位置:
ReadyMemoType=安装类型:
ReadyMemoComponents=选定组件:
ReadyMemoGroup=程序菜单文件夹:
ReadyMemoTasks=附加任务:

WizardInstalling=正在安装
InstallingLabel=安装程序正在将 [name] 安装到您的计算机中，请稍候。

WizardInfoBefore=信息
InfoBeforeLabel=请在继续之前阅读以下重要信息。
InfoBeforeClickLabel=当您准备好继续安装时，请单击“下一步”。

WizardInfoAfter=信息
InfoAfterLabel=请在继续之前阅读以下重要信息。
InfoAfterClickLabel=当您准备好继续安装时，请单击“下一步”。

WizardFinished=安装向导完成
FinishedHeadingLabel=[name] 安装向导完成
FinishedLabelNoIcons=安装程序已在您的计算机中安装了 [name]。
FinishedLabel=安装程序已在您的计算机中安装了 [name]。可以通过选择已安装的图标来运行应用程序。
ClickFinish=单击“结束”退出安装程序。
FinishedRestartLabel=为了完成 [name] 的安装，安装程序必须重新启动您的计算机。您想现在重新启动吗?
FinishedRestartMessage=为了完成 [name] 的安装，安装程序必须重新启动您的计算机。%n%n您想现在重新启动吗?
ShowReadmeCheck=是，我想查看自述文件
YesRadio=&是，现在重新启动计算机
NoRadio=&否，我稍后重新启动计算机
RunEntryExec=运行 %1
RunEntryShellExec=查看 %1

; *** Setup common messages
ErrorCreatingDir=安装程序无法创建目录 "%1"
ErrorTooManyFilesInDir=无法在目录 "%1" 中创建文件，因为里面的文件太多了

; === 修复点：补全缺失的自定义消息 ===
[CustomMessages]
CreateDesktopIcon=创建桌面快捷方式(&D)
AdditionalIcons=附加图标:
LaunchProgram=运行 %1