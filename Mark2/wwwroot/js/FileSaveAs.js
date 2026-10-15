function FileSaveAs(filename, contentType, content) {
    var link = document.createElement('a');
    link.download = filename;
    link.href = "data:" + contentType + ";base64," + content;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}
