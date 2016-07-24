using UnityEngine;

class Chunk {
    public readonly Texture3D texture;
    public readonly int count;
    public readonly int size;
    
    public Chunk(int size) {
        this.size = size;
        texture = ChunkManager.CreateEmptyTexture(size);
        count = 0;
    }

    public Chunk(Chunk other) {
        size = other.size;
        texture = ChunkManager.CreateTexture(other.size);
        Graphics.CopyTexture(other.texture, texture);
        count = other.count;
    }
}
