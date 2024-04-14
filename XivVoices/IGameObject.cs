using System.Numerics;

namespace XivVoices
{
    public interface IGameObject
    {
        public string Name { get; }
        public Vector3 Position { get; }
        public float Rotation { get; }
        public Vector3 Forward { get; }
        public Vector3 Top { get; }
        public string FocusedPlayerObject { get; }
    }
}