"""One-shot migration: convert each pair of label-cell + value-cell Borders
in FindVehiclePage.xaml into a single Border whose content is one read-only
TextBox containing "label-padded + value" — so dragging selects both as one
continuous string.

WPF .NET 6's TextBlock does not support IsTextSelectionEnabled (only WinUI
/ .NET 7+ does), and a TextBox can't contain styled inline Runs. So the
trade-off is: cross-cell drag-select works, but the label and value share
one color (the label still differentiates by being in monospace + padded).

Pattern matched:
  <Border Grid.Row="N" Grid.Column="0" Style="{StaticResource LabelBorder}">
    <TextBox Text="LABEL" Style="{StaticResource LabelText}" />
  </Border>
  <Border Grid.Row="N" Grid.Column="1" Style="{StaticResource ValueBorder}">
    <TextBox Text="{Binding PROP}" Style="{StaticResource ValueText}" .../>
  </Border>

Replaced with:
  <Border Grid.Row="N" Grid.ColumnSpan="2" Style="{StaticResource RowBorder}">
    <TextBox Style="{StaticResource RowText}"
             Text="{Binding PROP, StringFormat='LABEL_PADDED{0}'}" />
  </Border>
"""
import re
from pathlib import Path

XAML = Path(__file__).resolve().parent.parent / "VKdesktopapp" / "Records" / "FindVehiclePage.xaml"
LABEL_WIDTH = 18  # characters — wide enough for the longest label "Cust. Contact :"

PATTERN = re.compile(
    r'<Border\s+Grid\.Row="(?P<row>\d+)"\s+Grid\.Column="0"\s+Style="\{StaticResource\s+LabelBorder\}"\s*>'
    r'<TextBox\s+Text="(?P<label>[^"]+)"\s+Style="\{StaticResource\s+LabelText\}"\s*/>'
    r'</Border>\s*'
    r'<Border\s+Grid\.Row="(?P=row)"\s+Grid\.Column="1"\s+Style="\{StaticResource\s+ValueBorder\}"\s*>'
    r'<TextBox\s+Text="\{Binding\s+(?P<prop>[^}]+)\}"\s+Style="\{StaticResource\s+ValueText\}"(?P<extra>[^/]*)/>'
    r'</Border>',
    re.DOTALL,
)


def build_replacement(m: re.Match) -> str:
    row   = m.group("row")
    label = m.group("label").strip()
    prop  = m.group("prop").strip()
    padded = label.ljust(LABEL_WIDTH)
    # In XAML, single-quote inside a Binding StringFormat is escaped as &apos;
    # The format string itself uses braces: {}{0} (the empty {} prefix prevents
    # XAML from confusing literal braces with markup extensions).
    return (
        f'<Border Grid.Row="{row}" Grid.Column="0" Grid.ColumnSpan="2" Style="{{StaticResource RowBorder}}">'
        f'<TextBox Style="{{StaticResource RowText}}" '
        f'Text="{{Binding {prop}, StringFormat=&apos;{padded}{{0}}&apos;}}"/>'
        f'</Border>'
    )


def main() -> None:
    text = XAML.read_text(encoding="utf-8")
    new, n = PATTERN.subn(build_replacement, text)
    if n == 0:
        print("[convert] no rows matched - pattern needs tweaking")
        return

    # Replace the LabelBorder/ValueBorder/LabelText/ValueText styles with new
    # RowBorder and RowText styles. Drop the obsolete ones in one block.
    new_styles_block = '''<Style x:Key="RowBorder" TargetType="Border">
                                    <Setter Property="BorderBrush"      Value="{StaticResource Gray200}" />
                                    <Setter Property="BorderThickness"  Value="0,0,0,1" />
                                    <Setter Property="Padding"          Value="14,9" />
                                </Style>
                                <Style x:Key="RowText" TargetType="TextBox">
                                    <Setter Property="IsReadOnly"       Value="True" />
                                    <Setter Property="BorderThickness"  Value="0" />
                                    <Setter Property="Padding"          Value="0" />
                                    <Setter Property="Background"       Value="Transparent" />
                                    <Setter Property="Foreground"       Value="{StaticResource Gray900}" />
                                    <Setter Property="FontFamily"       Value="Consolas" />
                                    <Setter Property="FontSize"         Value="13" />
                                    <Setter Property="FontWeight"       Value="SemiBold" />
                                    <Setter Property="TextWrapping"     Value="Wrap" />
                                    <Setter Property="Cursor"           Value="IBeam" />
                                    <Setter Property="IsReadOnlyCaretVisible" Value="False" />
                                </Style>'''
    # Insert the new styles right after the SectionDivider style closes.
    new = re.sub(
        r'(<Style x:Key="SectionDivider"[^>]*>.*?</Style>)',
        r'\1\n                                ' + new_styles_block,
        new,
        count=1,
        flags=re.DOTALL,
    )
    XAML.write_text(new, encoding="utf-8")
    print(f"[convert] converted {n} row pairs to single-TextBox rows")


if __name__ == "__main__":
    main()
