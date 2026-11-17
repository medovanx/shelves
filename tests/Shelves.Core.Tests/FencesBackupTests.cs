using Shelves.Core.Model;

namespace Shelves.Core.Tests;

public class FencesBackupTests
{
    const string Snapshot = """
        <?xml version="1.0" encoding="utf-8"?>
        <SnapshotInfo xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <GUID>DailySnapshot3</GUID>
          <Groups>
            <ArrayOfString><string /><string>5</string></ArrayOfString>
            <ArrayOfString><string>0</string><string>6|2518|0|672|580|0|Games` |\\.\DISPLAY1` |1280|0|1920|1080|0|0|255|0|` |-1|1|1|1|0|{X} |0|0|0|</string></ArrayOfString>
          </Groups>
          <Icons>
            <ArrayOfString><string>C:\D\b.lnk</string><string>6|2635|2|117|2|0|1280|0|\\.\DISPLAY1|1|0|0||0|1|1</string></ArrayOfString>
            <ArrayOfString><string>C:\D\a.lnk</string><string>6|2539|2|21|2|0|1280|0|\\.\DISPLAY1|1|0|0||0|1|1</string></ArrayOfString>
          </Icons>
        </SnapshotInfo>
        """;

    const string Reg = """
        Windows Registry Editor Version 5.00

        [HKEY_CURRENT_USER\Software\Stardock\Fences\Groups]
        @="5"
        "0"="6|2518|0|672|580|0|Games` |\\\\.\\DISPLAY1` |1280|0|1920|1080|0|0|255|0|` |-1|1|1|1|0|{X} |0|0|0|"

        [HKEY_CURRENT_USER\Software\Stardock\Fences\Icons]
        "C:\\D\\b.lnk"="6|2635|2|117|2|0|1280|0|\\\\.\\DISPLAY1|1|0|0||0|1|1"
        "C:\\D\\a.lnk"="6|2539|2|21|2|0|1280|0|\\\\.\\DISPLAY1|1|0|0||0|1|1"

        [HKEY_CURRENT_USER\Software\Stardock\Fences\Other]
        "x"="ignored"
        """;

    [Theory]
    [InlineData(Snapshot)]
    [InlineData(Reg)]
    public void Backups_give_the_same_fences(string text)
    {
        var data = FencesImport.ReadBackup(text);
        Assert.NotNull(data);
        var f = Assert.Single(FencesImport.Parse(data.Value.groups, data.Value.icons));
        Assert.Equal("Games", f.Title);
        Assert.Equal(@"\\.\DISPLAY1", f.Monitor);
        Assert.Equal([@"C:\D\a.lnk", @"C:\D\b.lnk"], f.Items);
    }

    [Fact]
    public void Other_files_are_refused()
    {
        Assert.Null(FencesImport.ReadBackup("hello"));
        Assert.Null(FencesImport.ReadBackup("<SnapshotInfo><Groups/></SnapshotInfo>"));
        Assert.Null(FencesImport.ReadBackup("<SnapshotInfo><broken"));
    }
}
