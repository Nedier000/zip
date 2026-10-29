import os, glob
from ..streams import EndianBinaryReader
from ..files import File


def get_resource_data(*args):
    """
    Input:
    Option 1:
        0 - path - file path
        1 - assets_file - SerializedFile
        2 - offset -
        3 - size -
    Option 2:
        0 - reader - EndianBinaryReader
        1 - offset -
        2 - size -

    -> -2 = offset, -1 = size
    """
    if len(args) == 4:
        reader = search_resource(res_path=args[0], assets_file=args[1])
    elif len(args) == 3:
        reader = args[0]
    else:
        raise TypeError(f"3 or 4 arguments required, but only {len(args)} given")

    reader.Position = args[-2]
    return reader.read_bytes(args[-1])


def search_resource(res_path, assets_file):
    """Resolve Unity external texture data without accidentally reading the assets file.

    Unity bundles commonly store resource entries under a normalized/prefixed key,
    while Texture2D.m_StreamData.path contains only a relative path.  Match both
    the full path and basename, then try Unity's .resource/.resS alternate name.
    """
    base_name = os.path.basename(str(res_path).replace("\\", "/"))
    if base_name.endswith(".resource"):
        alternate_name = base_name[:-len(".resource")] + ".assets.resS"
    elif base_name.endswith(".assets.resS"):
        alternate_name = base_name[:-len(".assets.resS")] + ".resource"
    else:
        alternate_name = base_name

    files = getattr(getattr(assets_file, "parent", None), "files", {})
    candidates = [str(res_path), base_name, alternate_name]
    # Exact matches first, preserving original priority.
    for candidate in candidates:
        reader = files.get(candidate)
        if reader is not None:
            if isinstance(reader, File.File):
                reader = reader.reader
            return reader

    # Bundle entry keys can include directory prefixes or use normalized slashes.
    wanted = {os.path.basename(x.replace("\\", "/")).lower() for x in candidates}
    for key, reader in files.items():
        normalized = str(key).replace("\\", "/")
        if os.path.basename(normalized).lower() in wanted:
            if isinstance(reader, File.File):
                reader = reader.reader
            return reader

    # Try adjacent/extracted resource files when the bundle does not contain it.
    c = assets_file
    path = getattr(assets_file, "path", None)
    while not path:
        c = getattr(c, "parent", None)
        if c is None:
            break
        path = getattr(c, "path", None)
    if path:
        current_directory = path
        resource_file_path = os.path.join(current_directory, *str(res_path).replace("\\", "/").split("/"))
        if not os.path.isfile(resource_file_path):
            resource_file_path = search_resource_file(current_directory, base_name)
        if not os.path.isfile(resource_file_path):
            resource_file_path = search_resource_file(current_directory, alternate_name)
        if os.path.isfile(resource_file_path):
            return EndianBinaryReader(open(resource_file_path, "rb"))

    raise FileNotFoundError(
        f"Can't find external texture resource '{res_path}' (basename '{base_name}')"
    )

def search_resource_file(path, name):
    #print("real file", path, name)
    files = glob.glob(os.path.join(path, "**", name), recursive=True)
    return files[0] if len(files) else ""

