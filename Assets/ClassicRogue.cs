using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

public struct Coord : IEquatable<Coord>
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool Equals(Coord a) => X == a.X && Y == a.Y;
    
    public bool Equals(object a)
    {
        if (!(a is Coord)) return false;
        return Equals((Coord)a);
    }

    public static bool operator==(Coord a, Coord b)
    {
        return a.X == b.X && a.Y == b.Y;
    }

    public static bool operator !=(Coord a, Coord b)
    {
        return !(a == b);
    }

    public static Coord operator +(Coord a, Coord b)
    {
        return new Coord(a.X + b.X, a.Y + b.Y);
    }
    
    public Coord(int x, int y)
    {
        X = x;
        Y = y;
    }
}

public struct Tile
{
    public enum eBlock
    {
        None = 0,
        Wall,
        Entity
    }

    public Coord Position { get; set; }
    public eBlock BlockData { get; set; }
    public bool IsBlocking => BlockData != eBlock.None; 

    public Tile(Coord pos)
    {
        Position = pos;
        BlockData = eBlock.None;
    }
}

public class Creature
{
    public char Glyph { get; set; }
    public Coord Position { get; set; }
    public int Speed { get; set; }
}

public struct ViewTileCachedComponents
{
    public Renderer Renderer { get; set; }
}

public static class Extensions
{
    public static void Shuffle<T>(this IList<T> list)
    {
        for(var i=0; i < list.Count - 1; i++)
            list.Swap(i, UnityEngine.Random.Range(i, list.Count));
    }

    public static void Swap<T>(this IList<T> list, int i, int j)
    {
        var temp = list[i];
        list[i] = list[j];
        list[j] = temp;
    }
}

public class ClassicRogue : MonoBehaviour
{
    const int ViewWidthTiles = 80;
    const int ViewHeightTiles = 25;
    const int TileWidthPixels = 9;
    const int TileHeightPixels = 16;
    const int PixelsPerUnit = 100;

    const float TileWidthWS = TileWidthPixels / (float)PixelsPerUnit;
    const float TileHeightWS = TileHeightPixels / (float)PixelsPerUnit;


    const float TextureWidthPixels = 304;
    const float TextureHeightPixels = 144;
    const float TileWidthUV = TileWidthPixels / TextureWidthPixels;
    const float TileHeightUV = TileHeightPixels / TextureHeightPixels;
    
    const float ViewportWidthWS = TileWidthWS * ViewWidthTiles;
    const float ViewportHeightWS = TileHeightWS * ViewHeightTiles;
    const int ViewportHeightPixels = TileHeightPixels * ViewHeightTiles;
    const int ViewportWidthPixels = TileWidthPixels * ViewWidthTiles;
    
    const float ViewportRatio = (float)ViewportWidthPixels / (float)ViewportHeightPixels;

    private const int MaxCreatures = 150;
    
    public MaterialPropertyBlock MaterialPropertyBlock { get; set; }
    

    private Coord[] TileNeighbourOffsets = new Coord[]
    {
        new Coord(-1, 1),
        new Coord(0, 1),
        new Coord(1, 1),
        new Coord(1, 0),
        new Coord(1, -1),
        new Coord(0, -1),
        new Coord(-1, -1),
        new Coord(-1, 0),
    };


    public Material MatASCII;
    
    private Camera _camera;
    private GameObject[] _viewGO = new GameObject[ViewWidthTiles * ViewHeightTiles];
    private ViewTileCachedComponents[] _viewCache = new ViewTileCachedComponents[ViewWidthTiles * ViewHeightTiles];
    
    // Make things simpler by having a world the same size as the viewport.
    private char[] _viewport = new char[ViewWidthTiles * ViewHeightTiles];
    private Tile[] _worldTiles = new Tile[ViewWidthTiles * ViewHeightTiles];
    private Creature[] _creatureList;
    private Vector4[] _asciiToUV = new Vector4[256];
    
    const float SecondsPerFrame = 0.5f;
    private float _elaspedTime = SecondsPerFrame;    
    private int ShaderIdScaleTransform = -1;
    private Coord[] _validMoves;
    
    public static Coord IndexToXY(int i, int w) => new Coord(i % w, i / w);
    public static int XYToIndex(Coord xy, int w) =>  (xy.Y * w) + xy.X;
    

    public static Vector2 ASCIICodeToUV(int asciiCode)
    {
        const int charsPerLineInTexture = 32;
        
        const float texOffsetX = 8.0f;
        const float texOffsetY = 8.0f + TileHeightPixels; // at 8.0f y is at the bottom of the first character, add char height to move it to the top of the character.


        const float texOffsetU = texOffsetX / TextureWidthPixels;
        const float texOffsetV = texOffsetY / TextureHeightPixels;
        Coord charCoord = IndexToXY(asciiCode, charsPerLineInTexture);
        
        return new Vector2(
            texOffsetU + TileWidthUV * charCoord.X,
            1.0f - (texOffsetV + TileHeightUV * charCoord.Y));
    }
    
    public char TileToASCII(Tile t)
    {
        foreach (Creature c in _creatureList)
        {
            if (c.Position == t.Position)
                return c.Glyph;
        }
        
        return t.IsBlocking ? '#' : '.';
    }

    public bool IsTilePosValid(Coord pos)
    {
        int index = XYToIndex(pos, ViewWidthTiles);
        return index >= 0 && index < _worldTiles.Length;
    }

    public bool IsTileBlocked(Coord position)
    {
        int index = XYToIndex(position, ViewWidthTiles);
        return _worldTiles[index].IsBlocking;
    }

    void FitScreen()
    {
        float screenRatio = (float)Screen.width / (float)Screen.height;
        
        if (screenRatio >= ViewportRatio)
        {
            _camera.orthographicSize = ViewportHeightWS / 2;
        }
        else
        {
            float differenceInSize = ViewportRatio / screenRatio;
            _camera.orthographicSize = ViewportHeightWS / 2 * differenceInSize;
        }
    }
    
    // Start is called before the first frame update
    void Start()
    {
        ShaderIdScaleTransform = Shader.PropertyToID("_MainTex_ST");
        MaterialPropertyBlock = new MaterialPropertyBlock();
        
        // Store UV positions for ASCII glyphs
        for (int i = 0; i < _asciiToUV.Length; i++)
        {
            Vector2 uvPos = ASCIICodeToUV((char) i);
            _asciiToUV[i] = new Vector4(TileWidthUV, TileHeightUV, uvPos.x, uvPos.y);
        }

        // Setup the Camera
        _camera = Camera.main;
        _camera.transform.position = new Vector3(0,0, -1);
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.orthographic = true;

        FitScreen();
        
        // Create Tiles for Viewport
        float tileX = -(ViewportWidthWS - TileWidthWS) / 2.0f;
        float tileY = (ViewportHeightWS - TileHeightWS) / 2.0f;
        for (int i = 0; i < (ViewWidthTiles * ViewHeightTiles); i++)
        {
            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tile.transform.parent = transform;
            tile.transform.localScale = new Vector3(TileWidthWS, TileHeightWS, 1);
            Coord viewXY = IndexToXY(i, ViewWidthTiles);
            tile.transform.position = new Vector3(tileX + (viewXY.X * TileWidthWS), tileY - (viewXY.Y * TileHeightWS));
            Renderer renderer = tile.GetComponent<Renderer>();
            renderer.sharedMaterial = MatASCII;
            _viewGO[i] = tile;
            _worldTiles[i] = new Tile(viewXY);
            _viewCache[i] = new ViewTileCachedComponents
            {
                Renderer = renderer,
            };
        }

        // Do some level creation - random wall tiles
        UnityEngine.Random.InitState(42);

        List<int> emptyTiles = new List<int>(Mathf.FloorToInt(_worldTiles.Length * 0.8f));
        for (int i = 0; i < _worldTiles.Length; i++)
        {
            bool blocking = (UnityEngine.Random.value > 0.8f);
            
            if(blocking)
                _worldTiles[i].BlockData = Tile.eBlock.Wall;
            else
                emptyTiles.Add(i);
        }
        
        emptyTiles.Shuffle();

        // Add creatures
        _creatureList = new Creature[Mathf.Min(emptyTiles.Count, MaxCreatures)];
        for (int i = 0; i < _creatureList.Length; i++)
        {
            int index = emptyTiles[i];
  
            _creatureList[i] = new Creature
            {
                Position = _worldTiles[index].Position,
                Glyph = 'r',
                Speed = Mathf.FloorToInt(UnityEngine.Random.value * 10)
            };
            // Set block data on tile.
            _worldTiles[index].BlockData = Tile.eBlock.Entity;
        }
        
        // Create movement data
        _validMoves  = new Coord[TileNeighbourOffsets.Length];
    }
    
    
    
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Escape))
            Application.Quit();
            
        // Only update every SecondsPerFrame
        _elaspedTime += Time.deltaTime;
        if (_elaspedTime < SecondsPerFrame)
            return;
        else
            _elaspedTime = _elaspedTime - SecondsPerFrame;
        
        // This is the bit I'm interest to optimize and compare against DOTS
        Profiler.BeginSample("creature_movement");
        
        Array.Sort(_creatureList, (x, y) => x.Speed.CompareTo(y.Speed));
        
        // Do movements one by one
        for (int i = 0; i < _creatureList.Length; i++)
        {
            // Get surrounding tiles, that have no blockers
            int validMoveFillAmount = 0;
            for (int j = 0; j < TileNeighbourOffsets.Length; j++)
            {
                Coord posToCheck = _creatureList[i].Position + TileNeighbourOffsets[j];
                if (IsTilePosValid(posToCheck) && !IsTileBlocked(posToCheck))
                {
                    _validMoves[validMoveFillAmount] = posToCheck;
                    validMoveFillAmount++;
                }
            }
            // Choose one at random
            if (validMoveFillAmount > 0)
            {
                Coord prevPos = _creatureList[i].Position;
                _creatureList[i].Position = _validMoves[UnityEngine.Random.Range(0, validMoveFillAmount)];
                _worldTiles[XYToIndex(prevPos, ViewWidthTiles)].BlockData = Tile.eBlock.None;
                _worldTiles[XYToIndex(_creatureList[i].Position, ViewWidthTiles)].BlockData = Tile.eBlock.Entity;
            }
        }
        
        Profiler.EndSample();
        
        // Update Visuals
        for (int i = 0; i < _viewport.Length; i++)
            _viewport[i] = _worldTiles[i].IsBlocking ? '#' : '.';

        for (int i = 0; i < _creatureList.Length; i++)
            _viewport[XYToIndex(_creatureList[i].Position, ViewWidthTiles)] = _creatureList[i].Glyph;
        
        for (int i = 0; i < _viewport.Length; i++)
        {
            Renderer renderer = _viewCache[i].Renderer;
            
            renderer.GetPropertyBlock(MaterialPropertyBlock);
            MaterialPropertyBlock.SetVector(ShaderIdScaleTransform, _asciiToUV[_viewport[i]]);
            renderer.SetPropertyBlock(MaterialPropertyBlock);
        }
    }

    private void LateUpdate()
    {
        FitScreen();
    }
}
