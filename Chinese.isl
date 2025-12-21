; *** Inno Setup version 6.0.0+ Chinese Simplified messages ***
;
; Authors: KngStr, 2024 (Updated for IS 6)
;

[LangOptions]
LanguageName=Chinese Simplified
LanguageID=$0804
LanguageCodePage=936
; 下面这两个字体设置对于现代 Windows 很重要，防止乱码或字体过小
DialogFontName=Microsoft YaHei UI
DialogFontSize=9
WelcomeFontName=Microsoft YaHei UI
WelcomeFontSize=12
TitleFontName=Microsoft YaHei UI
TitleFontSize=29
CopyrightFontName=Microsoft YaHei UI
CopyrightFontSize=8

[Messages]

; *** Application startup messages
SetupAppTitle=安装
SetupWindowTitle=安装 - %1
UninstallAppTitle=卸载
UninstallAppFullTitle=%1 卸载

; *** Misc. errors
ErrorInternal2=内部错误: %1
ErrorFunctionFailedNoCode=%1 失败
ErrorFunctionFailed=%1 失败; 代码 %2
ErrorFunctionFailedWithMessage=%1 失败; 代码 %2.%n%3
ErrorExecutingProgram=无法执行文件:%n%1

; *** SetupLdr messages
SetupLdrStartupMessage=这将安装 %1。是否继续?
LdrCannotCreateTemp=无法创建临时文件。安装中止
LdrCannotExecTemp=无法执行临时文件夹中的文件。安装中止

; *** Startup error messages
LastErrorMessage=%1.%n%n错误 %2: %3
SetupFileMissing=安装目录中的文件 %1 丢失。请更正这个问题或获取新的程序副本。
SetupFileCorrupt=安装文件已损坏。请获取新的程序副本。
SetupFileCorruptOrWrongVer=安装文件已损坏，或是与此版本的安装程序不兼容。请更正这个问题或获取新的程序副本。
InvalidParameter=无效的命令行参数: %n%1
SetupAlreadyRunning=安装程序正在运行。
WindowsVersionNotSupported=本程序不支持您的 Windows 版本。
WindowsServicePackRequired=本程序需要 %1 Service Pack %2 或更高版本。
NotOnThisPlatform=本程序不能在 %1 上运行。
OnlyOnThisPlatform=本程序必须在 %1 上运行。
OnlyOnTheseArchitectures=本程序只能在专门为下列处理器架构设计的 Windows 版本中进行安装:%n%n%1
WinVersionTooLowError=本程序需要 %1 版本 %2 或更高。
WinVersionTooHighError=本程序不能安装于 %1 版本 %2 或更高。
AdminPrivilegesRequired=您在安装本程序时必须以管理员身份登录。
PowerUserPrivilegesRequired=您在安装本程序时必须以管理员或有权限的用户身份登录。
SetupAppRunningError=安装程序检测到 %1 正在运行。%n%n请关闭所有它的实例，然后点击“确定”继续，或点击“取消”退出。
UninstallAppRunningError=卸载程序检测到 %1 正在运行。%n%n请关闭所有它的实例，然后点击“确定”继续，或点击“取消”退出。

; *** Startup questions
PrivilegesRequiredOverrideTitle=选择安装模式
PrivilegesRequiredOverrideInstruction=选择安装模式
PrivilegesRequiredOverrideText1=%1 可以为所有用户安装(需要管理员权限)，或仅为您安装。
PrivilegesRequiredOverrideText2=%1 可以仅为您安装，或为所有用户安装(需要管理员权限)。
PrivilegesRequiredOverrideAllUsers=为所有用户安装 (推荐)(&A)
PrivilegesRequiredOverrideAllUsersRecommended=为所有用户安装 (推荐)(&A)
PrivilegesRequiredOverrideCurrentUser=仅为我安装(&M)
PrivilegesRequiredOverrideCurrentUserRecommended=仅为我安装 (推荐)(&M)

; *** Misc. errors
ErrorOpeningReadme=当尝试打开 README 文件时发生错误。
ErrorCreatingTemp=当尝试在目标目录创建临时文件时发生错误。
ErrorReadingSource=当尝试读取源文件时发生错误。
ErrorCopying=当尝试复制文件时发生错误。
ErrorRenamingTemp=当尝试重命名目标目录中的临时文件时发生错误。
ErrorDeletingExisting=当尝试删除现有的文件时发生错误。
ErrorWriting=当尝试写入文件时发生错误。
ErrorAccessDenied=拒绝访问。
ErrorRenameAccessDenied=重命名文件失败: 拒绝访问。
ErrorFillAccessDenied=填充文件失败: 拒绝访问。
ErrorCloseApplications=安装程序无法自动关闭所有应用程序。建议您在继续安装之前先关闭所有使用被安装程序文件的应用程序。
ErrorRestartingComputer=安装程序无法重启电脑。请手动重启。
ErrorRestartReplace=重启置换失败:
ErrorRenameExisting=无法重命名现有文件。
ErrorReadingExistingDest=当尝试读取现有文件时发生错误。
ErrorReadingExistingDestOrSource=当尝试读取现有文件或源文件时发生错误。
ErrorInValidOpenMode=文件无法以只读模式打开。
ErrorParentDirNotExists=父目录不存在。
ErrorUserCancel=用户取消了操作。
ErrorInvalidSourceDir=源目录无效。
ErrorBadInstallation=无效的安装。请卸载之后重新安装。
ErrorRestartingComputer2=安装程序无法重启电脑。请手动重启。

; *** Post-installation errors
ErrorRegisterServer=无法注册 DLL/OCX: %1
ErrorRegSvr32Failed=RegSvr32 失败，返回代码 %1
ErrorRegisterTypeLib=无法注册类型库: %1
ErrorOpeningIniFile=当尝试打开 INI 文件时发生错误: %1
ErrorIniEntry=无法在 INI 文件中创建条目 "%1"。
ErrorAccessingIniFile=当尝试写入 INI 文件时发生错误: %1

; *** Uninstaller messages
UninstallNotFound=文件 "%1" 不存在。无法卸载。
UninstallOpenError=文件 "%1" 无法打开。无法卸载
UninstallUnsupportedVer=卸载日志文件 "%1" 的格式不能被此版本的卸载程序识别。无法卸载
UninstallUnknownEntry=在卸载日志中遇到一个未知的条目 (%1)
UninstallProblem=卸载日志文件 "%1" 存在问题。无法卸载
UninstallDataCorrupted=文件 "%1" 数据已损坏。无法卸载
FileNotFound=文件未找到
CannotInstallToNetworkDrive=安装程序无法安装到网络驱动器。
CannotInstallToUNCPath=安装程序无法安装到 UNC 路径。
CannotContinue=安装程序无法继续。请点击“取消”退出。
ApplicationsFound=下列应用程序正在使用本安装程序需要更新的文件。建议您允许安装程序自动关闭这些应用程序。
ApplicationsFound2=下列应用程序正在使用本安装程序需要更新的文件。建议您允许安装程序自动关闭这些应用程序。安装完成后，安装程序将尝试重新启动这些应用程序。
CloseApplications=自动关闭应用程序(&A)
DontCloseApplications=不要关闭应用程序(&D)
ErrorCloseApplications=安装程序无法自动关闭所有应用程序。建议您在继续安装之前先关闭所有使用需要更新文件的应用程序。
PrepareToInstallNeedsRestart=安装程序必须重启计算机才能继续。重启后，请再次运行安装程序以完成安装。%n%n您想现在重启吗?

; *** Wizard common messages
ClickNext=点击“下一步”继续，或点击“取消”退出安装。
BeveledLabel=
BrowseDialogTitle=浏览文件夹
BrowseDialogLabel=在下面的列表中选择一个文件夹，然后点击“确定”。
NewFolderName=新文件夹

; *** Wizard "Welcome" & "Finished" pages
WelcomeLabel1=欢迎使用 [name] 安装向导
WelcomeLabel2=本向导将在您的电脑上安装 [name/ver]。%n%n建议您在继续之前关闭所有其它应用程序。
WizardFinishedLabel=安装程序已在您的电脑中安装了 [name]。本应用程序可以通过选择安装的快捷方式运行。
WizardFinishedLabel2=安装程序已在您的电脑中安装了 [name]。
ClickFinish=点击“完成”退出安装。
ClickYes=是
ClickNo=否
FinishedRestartLabel=为了完成 [name] 的安装，安装程序必须重启您的电脑。您想现在重启吗?
FinishedRestartError=为了完成 [name] 的安装，安装程序必须重启您的电脑。%n%n请手动重启。
FinishedRestartOption=是，现在重启电脑(&Y)
FinishedNoRestartOption=否，我稍后重启电脑(&N)

; *** Wizard "Setup Password" page
WizardPassword=密码
PasswordLabel1=本安装程序受密码保护。
PasswordLabel3=请输入密码，密码区分大小写。然后点击“下一步”继续。
PasswordEditLabel=密码(&P):
IncorrectPassword=您输入的密码不正确，请重试。

; *** Wizard "License Agreement" page
WizardLicense=许可协议
LicenseLabel=继续安装前请阅读下列重要信息。
LicenseLabel3=请仔细阅读下列许可协议。您在继续安装前必须同意这些协议条款。
LicenseAccepted=我同意此协议(&A)
LicenseNotAccepted=我不同意此协议(&D)

; *** Wizard "Information" pages
WizardInfoBefore=信息
InfoBeforeLabel=继续安装前请阅读下列重要信息。
InfoBeforeClickLabel=准备好继续安装后，点击“下一步”。
WizardInfoAfter=信息
InfoAfterLabel=继续安装前请阅读下列重要信息。
InfoAfterClickLabel=准备好继续安装后，点击“下一步”。

; *** Wizard "User Information" page
WizardUserInfo=用户信息
UserInfoDesc=请输入您的信息。
UserInfoName=用户名(&U):
UserInfoOrg=组织(&O):
UserInfoSerial=序列号(&S):
UserInfoNameRequired=您必须输入一个名称。

; *** Wizard "Select Destination Location" page
WizardSelectDir=选择目标位置
SelectDirDesc=您想将 [name] 安装在什么地方?
SelectDirLabel3=安装程序将把 [name] 安装在下列文件夹中。
SelectDirBrowseLabel=点击“下一步”继续。如果您想选择其它文件夹，点击“浏览”。
DiskSpaceGBLabel=至少需要 [gb] GB 的可用磁盘空间。
DiskSpaceMBLabel=至少需要 [mb] MB 的可用磁盘空间。
CannotInstallToPath=程序不能安装在那个文件夹，因为这不满足条件。
InvalidPath=您必须输入一个完整的路径，并带盘符; 例如:%n%nC:\APP%n%n或一个 UNC 路径:%n%n\\server\share
InvalidDrive=您选择的驱动器或 UNC 共享不存在或不能访问。请另外选择。
DiskSpaceWarning=安装程序至少需要 %1 KB 的可用磁盘空间才能安装，但选定驱动器只有 %2 KB 的可用空间。%n%n您确定要继续吗?
DirNameTooLong=文件夹名称或路径太长。
InvalidDirName=文件夹名称无效。
BadDirName32=文件夹名称不能包含下列字符之一:%n%n%1
DirExistsTitle=文件夹已存在
DirExists=文件夹:%n%n%1%n%n已经存在。您想安装到那个文件夹吗?
DirDoesntExistTitle=文件夹不存在
DirDoesntExist=文件夹:%n%n%1%n%n不存在。您想创建该文件夹吗?

; *** Wizard "Select Components" page
WizardSelectComponents=选择组件
SelectComponentsDesc=您想安装哪些组件?
SelectComponentsLabel2=选择您想安装的组件; 清除您不想安装的组件。然后点击“下一步”继续。
FullInstallation=完全安装
CompactInstallation=精简安装
CustomInstallation=自定义安装
TypesComboLabel=安装类型(&T):
ComponentsListLabel=组件(&C):
ComponentsDiskSpaceGBLabel=当前选择至少需要 [gb] GB 的磁盘空间。
ComponentsDiskSpaceMBLabel=当前选择至少需要 [mb] MB 的磁盘空间。

; *** Wizard "Select Additional Tasks" page
WizardSelectTasks=选择附加任务
SelectTasksDesc=您想选择哪些附加任务?
SelectTasksLabel2=选择您想在安装 [name] 时执行的附加任务，然后点击“下一步”。

; *** Wizard "Select Start Menu Folder" page
WizardSelectProgramGroup=选择开始菜单文件夹
SelectStartMenuFolderDesc=您想在哪里放置程序的快捷方式?
SelectStartMenuFolderLabel3=安装程序将在下列“开始”菜单文件夹中创建程序的快捷方式。
SelectStartMenuFolderBrowseLabel=点击“下一步”继续。如果您想选择其它文件夹，点击“浏览”。
MustEnterGroupName=您必须输入一个文件夹名称。
GroupNameTooLong=文件夹名称或路径太长。
InvalidGroupName=文件夹名称无效。
BadGroupName=文件夹名称不能包含下列字符之一:%n%n%1
NoProgramGroupCheck2=不创建“开始”菜单文件夹(&D)

; *** Wizard "Ready to Install" page
WizardReady=准备安装
ReadyLabel1=安装程序准备开始在您的电脑上安装 [name]。
ReadyLabel2a=点击“安装”继续，或点击“上一步”检查或更改设置。
ReadyLabel2b=点击“安装”继续，或点击“上一步”检查或更改设置。
ReadyMemoUserInfo=用户信息:
ReadyMemoDir=目标位置:
ReadyMemoType=安装类型:
ReadyMemoComponents=选定组件:
ReadyMemoGroup=“开始”菜单文件夹:
ReadyMemoTasks=附加任务:

; *** Wizard "Preparing to Install" page
WizardPreparing=正在准备安装
PreparingDesc=安装程序正在准备安装 [name]。
PreviousInstallNotCompleted=先前程序的安装/卸载未完成。您需要重启电脑才能完成安装。%n%n重启电脑后，请再次运行安装程序来完成安装 [name]。
CannotContinue=安装程序不能继续。请点击“取消”退出。
ApplicationsFound=下列应用程序正在使用安装程序需要更新的文件。建议您允许安装程序自动关闭这些应用程序。
ApplicationsFound2=下列应用程序正在使用安装程序需要更新的文件。建议您允许安装程序自动关闭这些应用程序。安装完毕后，安装程序将尝试重启应用程序。
CloseApplications=自动关闭应用程序(&A)
DontCloseApplications=不要关闭应用程序(&D)
ErrorCloseApplications=安装程序无法自动关闭所有应用程序。建议您在继续之前关闭所有使用需更新文件的应用程序。

; *** Wizard "Installing" page
WizardInstalling=正在安装
InstallingLabel=安装程序正在安装 [name]，请稍候。

; *** Wizard "Info After" page
WizardInfoAfter=信息
InfoAfterLabel=继续安装前请阅读下列重要信息。
InfoAfterClickLabel=准备好继续后，点击“下一步”。

; *** Wizard "Setup Completed" page
WizardSetupCompleted=安装完成
SetupCompletedLabel=安装程序已在您的电脑中安装了 [name]。本应用程序可以通过选择安装的快捷方式运行。
SetupCompletedLabel2=安装程序已在您的电脑中安装了 [name]。
ClickFinish=点击“完成”退出安装。
FinishedRestartLabel=为了完成 [name] 的安装，安装程序必须重启您的电脑。您想现在重启吗?
FinishedRestartError=为了完成 [name] 的安装，安装程序必须重启您的电脑。%n%n请手动重启。
FinishedRestartOption=是，现在重启电脑(&Y)
FinishedNoRestartOption=否，我稍后重启电脑(&N)

; *** "Select Language" dialog messages
SelectLanguageTitle=选择安装语言
SelectLanguageLabel=选择安装时要使用的语言。

; *** Common messages
ConfirmTitle=确认
ConfirmCancelMessage=您确定要完全取消安装吗?
ActionConfirmation=您确定要执行这个动作吗?
InfoTitle=信息
ErrorTitle=错误
ErrorMessage=错误: %1
ErrorNumber=错误 %1
Retry=重试(&R)
Ignore=忽略(&I)
Abort=中止(&A)
Cancel=取消
OK=确定
Yes=是(&Y)
No=否(&N)
MessageboxTitle=信息
MessageboxErrorTitle=错误
MsgBoxRetryCancel=重试;取消
MsgBoxAbortRetryIgnore=中止;重试;忽略
MsgBoxYesNo=是;否
MsgBoxYesNoCancel=是;否;取消
DiskSpaceWarning=磁盘空间不足
DirNameTooLong=目录名称太长
InvalidDirName=目录名称无效
BadDirName32=目录名称无效
DirExists=目录已存在
DirDoesntExist=目录不存在

; *** Custom messages
; (None - you can add your custom messages here)

[CustomMessages]
; Add your own custom messages here if needed.
LaunchProgram=运行 %1
AdditionalIcons=附加图标:
CreateDesktopIcon=创建桌面快捷方式(&D)