#!/usr/bin/env python3
"""Validate the portable presentation using only Python's standard library and Node."""
import json
from html.parser import HTMLParser
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent
PUBLIC = ROOT / 'public'


class Document(HTMLParser):
    def __init__(self, path):
        super().__init__()
        self.references = []
        self.slides = []
        self.scripts = []
        self.footer_links = []
        self._script = None
        self._footer = False
        self.feed(path.read_text())

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        for key in ('src', 'href'):
            if key in attrs:
                self.references.append(attrs[key])
        if tag == 'section' and 'slide' in attrs.get('class', '').split():
            self.slides.append(attrs.get('id'))
        if tag == 'footer':
            self._footer = True
        if self._footer and tag == 'a':
            self.footer_links.append(attrs.get('href', ''))
        if tag == 'script' and 'src' not in attrs:
            self._script = []

    def handle_endtag(self, tag):
        if tag == 'footer':
            self._footer = False
        if tag == 'script' and self._script is not None:
            self.scripts.append(''.join(self._script))
            self._script = None

    def handle_data(self, value):
        if self._script is not None:
            self._script.append(value)


def verify():
    story = json.loads((ROOT / 'story.json').read_text())
    chapters = story['chapters']
    assert len(chapters) == 6
    with tempfile.TemporaryDirectory() as temp:
        for filename in ('slides.html', 'teleprompter.html'):
            path = PUBLIC / filename
            document = Document(path)
            for reference in document.references:
                assert not re.match(r'(https?:|//|file:)', reference), reference
                if reference.startswith(('#', 'data:')):
                    continue
                target = reference.split('#', 1)[0].split('?', 1)[0]
                assert (PUBLIC / target).is_file(), reference
            for reference in re.findall(r'url\(["\']?([^\)"\']]+)', path.read_text()):
                assert (PUBLIC / reference).is_file(), reference
            for number, source in enumerate(document.scripts):
                script = Path(temp) / f'{filename}-{number}.js'
                script.write_text(source)
                subprocess.run(['node', '--check', str(script)], check=True, capture_output=True)
            if filename == 'slides.html':
                assert document.slides == [chapter['id'] for chapter in chapters]
                assert not any('teleprompter' in href for href in document.footer_links)
            else:
                source = document.scripts[0]
                start = source.index('const chapters=') + len('const chapters=')
                embedded, _ = json.JSONDecoder().raw_decode(source[start:])
                assert embedded == chapters, 'Teleprompter and story.json differ'
    notes = (PUBLIC / 'teleprompter.md').read_text()
    for chapter in chapters:
        assert chapter['title'] in notes
        assert chapter['cue'] in notes
        for paragraph in chapter['paragraphs']:
            assert paragraph in notes
    with zipfile.ZipFile(PUBLIC / 'Elsa-Studio-Demo-v4.pptx') as archive:
        assert archive.testzip() is None
        slides = [name for name in archive.namelist() if re.fullmatch(r'ppt/slides/slide\d+\.xml', name)]
        assert len(slides) == 6
        for name in slides:
            ET.fromstring(archive.read(name))
    print('Presentation verified: 6 chapters, local assets, valid scripts, matching notes, valid PowerPoint package.')


if __name__ == '__main__':
    verify()
