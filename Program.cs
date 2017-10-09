using System;
using System.IO;
using System.Collections.Generic;

namespace Project {
  class Program {
    static void Main(string[] args) {
      // 変数からファイル名を取り出し
      string filename = args[0];
      // ファイルを開く
      FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read);
      // ファイルサイズからいい感じにbyte配列を生成する
      int size = (int)fs.Length;
      byte[] buf = new byte[size];
      // データの読み込み
      int remainSize = size;
      int readSize = 0;
      while(remainSize != 0) {
        int read = fs.Read(buf, readSize, Math.Min(1024, remainSize));
        remainSize -= read;
        readSize += read;
      }

      List<byte> buffer = new List<byte>(buf);
      // データをクラスにパースする
      MIDI.MIDI midi = MIDI.MIDIFactory.Parse(buffer);
      Console.WriteLine(midi.header.ToString());

      fs.Dispose();
    }
  }

  namespace MIDI {
    class MIDI {
      public HeaderChunk header;
      public TrackChunk[] tracks;
      public MIDI(HeaderChunk header, TrackChunk[] tracks) {
        this.header = header;
        this.tracks = tracks;
      }
    }

    class Chunk {
      public enum ChunkType {
        UNDEFINED,
        HEADER,
        TRACK,
      }

      public ChunkType type;
      public int size = 0;

      public Chunk(List<byte> buf) {
        string rawType = System.Text.Encoding.UTF8.GetString(buf.GetRange(0, 4).ToArray());
        type = getType(rawType);
        size = Util.Int32(buf.GetRange(4, 4));
      }

      private ChunkType getType(string rawType) {
        switch(rawType) {
          case "MThd":
            return ChunkType.HEADER;
          case "MTrk":
            return ChunkType.TRACK;
          default:
            return ChunkType.UNDEFINED;
        }
      }

      public override string ToString() {
        return String.Format("type: {0}, size: {1}", type, size);
      }
    }

    class HeaderChunk: Chunk {
      // チャンクタイプ 4byte ... ("MThd")
      // データ長 4byte ... (6)
      // フォーマット 2byte ... (0 or 1 or 2)
      // トラック数 2byte
      // 時間単位 2byte
      public int format = -1;
      public int trackCount = 0;
      public int timeSlice = -1;
      public HeaderChunk(List<byte> buf): base(buf) {
        format = Util.Int16(buf.GetRange(8, 2));
        trackCount = Util.Int16(buf.GetRange(10, 2));
        timeSlice = Util.Int16(buf.GetRange(12, 2));
      }

      public override string ToString() {
        return String.Format("{0}, format: {1}, trackSize: {2}, timeSlice: {3}", base.ToString(), format, trackCount, timeSlice);
      }
    }

    class TrackChunk: Chunk {
      public class ChunkData { 
        public DeltaTime delta;
        public Event e;
        public ChunkData(DeltaTime delta, Event e) {
          this.delta = delta;
          this.e = e;
        }
      }
      public class DeltaTime {
        public int time = 0;
        public DeltaTime(int deltaTime) {
          time = deltaTime;
        }
      }

      public class Event {}

      public abstract class MIDIEvent: Event {}

      public class NoteOffEvent: MIDIEvent {
        public byte pitch = 0;
        public NoteOffEvent(byte pitch) {
          this.pitch = pitch;
        }
      }

      public class NoteOnEvent: MIDIEvent {
        public byte pitch = 0;
        public byte velocity = 0;
        public NoteOnEvent(byte pitch, byte velocity) {
          this.pitch = pitch;
          this.velocity = velocity;
        }
      }

      public class ControlChangeEvent: MIDIEvent {
        public byte data1 = 0;
        public byte data2 = 0;
        public ControlChangeEvent(byte data1, byte data2) {
          this.data1 = data1;
          this.data2 = data2;
        }
      }

      public class SysExEvent: Event {
        public int data = 0;
        public SysExEvent(int data) {
          this.data = data;
        }
      }

      public abstract class MetaEvent: Event {}
      public class TextEvent: MetaEvent {
        string text;
        public TextEvent(string text) {
          this.text = text;
          // Console.WriteLine(text);
        }
      }
      public class CommentEvent: TextEvent { public CommentEvent(string text):base(text) {} }
      public class CopyRightEvent: TextEvent { public CopyRightEvent(string text):base(text) {} }
      public class TrackNameEvent: TextEvent { public TrackNameEvent(string text):base(text) {} }
      public class InstrumentNameEvent: TextEvent { public InstrumentNameEvent(string text):base(text) {} }
      public class LyricEvent: TextEvent { public LyricEvent(string text):base(text) {} }

      public class TempoEvent: MetaEvent {
        int tempo = 0;
        public TempoEvent(int tempo) {
          this.tempo = tempo;
        }
      }

      public class BeatEvent: MetaEvent {
        public byte numerator = 0;
        public byte denominator = 0;
        public byte phoneticValue = 0;
        public byte note32Count = 0;
        public BeatEvent(byte numerator, byte denominator, byte phoneticValue, byte note32Count) {
          this.numerator = numerator;
          this.denominator = denominator;
          this.phoneticValue = phoneticValue;
          this.note32Count = note32Count;
        }
      }

      public class KeyEvent: MetaEvent {
        public byte sharpCount;
        public byte flatCount;
        public Boolean isMajor;
        public KeyEvent(byte sf, byte ml) {
          int i = sf;
          if (i > 0) {
            sharpCount = (byte)i;
          } else {
            flatCount = (byte)Math.Abs(i);
          }
          isMajor = ml == 0;
        }
      }

      public class EndEvent: MetaEvent {}

      List<ChunkData> datas;
      
      public TrackChunk(List<byte> buf): base(buf) {
        datas = ParseData(buf.GetRange(8, size));
        foreach (var data in datas) {
          Console.WriteLine(data.delta.time);
          Console.WriteLine(data.e.GetType());
        }
      }

      public struct VariableLengthData {
        public int data;
        public int length;
      }

      private VariableLengthData GetVariableLengthData(List<byte> buf) {
        var deltaHasNext = true;
        var data = 0;
        var i = 0;
        do {
          data = data << 7;
          deltaHasNext = (buf[i] & (byte)0b10000000) != 0;
          data += (buf[i] & (byte)0b01111111);
          i++;
        } while (deltaHasNext);
        VariableLengthData d;
        d.data = data;
        d.length = i;
        return d;
      }

      private List<ChunkData> ParseData(List<byte> buf) {
        var isEnd = false;
        var i = 0;
        var track = new List<ChunkData>();
        while (!isEnd) {
          DeltaTime d = null;
          Event e = null;
          {
            // parsing delta time
            var data = GetVariableLengthData(buf.GetRange(i, buf.Count - i));
            i += data.length;
            d = new DeltaTime(data.data);
          }
          {
            if (buf[i] < 0x80) {
              i += 5;
              continue;
            } else if (buf[i] >= 0x80 && buf[i] < 0x90) {
              // note off
              e = new NoteOffEvent(buf[++i]);
              i += 2;
            } else if (buf[i] < 0xA0) {
              // note on
              e = new NoteOnEvent(buf[++i], buf[++i]);
              i++;
            } else if (buf[i] < 0xF0) {
              // control change
              e = new ControlChangeEvent(buf[++i], buf[++i]);
              i++;
            } else if (buf[i] < 0xFF) {
              // SysEx event
              var data = GetVariableLengthData(buf.GetRange(++i, buf.Count - i));
              e = new SysExEvent(data.data);
              i += data.length;
            } else {
              i++;
              // meta event.
              if (buf[i] != 0x2F) {
                if (buf[i] < 0x51) {
                  // text
                  i++;
                  e = new TextEvent(System.Text.Encoding.UTF8.GetString(buf.GetRange(i + 1, buf[i]).ToArray()));
                  i += buf[i] + 1;
                } else if (buf[i] == 0x51) {
                  i++;
                  e = new TempoEvent(Util.Int24(buf.GetRange(++i, 3)));
                  i += 3;
                } else if (buf[i] == 0x58) {
                  i++;
                  e = new BeatEvent(buf[++i], buf[++i], buf[++i], buf[++i]);
                  i++;
                } else if (buf[i] == 0x59) {
                  i++;
                  e = new KeyEvent(buf[++i], buf[++i]);
                  i++;
                }
              } else {
                e = new EndEvent();
                isEnd = true;
              }
            }
          }
          track.Add(new ChunkData(d, e));
        }
        return track;
      }
    }

    class MIDIFactory {
      public static MIDI Parse(List<byte> buffer) {
        List<byte> rawHeader = buffer.GetRange(0, 14); 
        HeaderChunk header = new HeaderChunk(rawHeader);
        List<TrackChunk> chunks = CreateTrackChunks(buffer.GetRange(14, buffer.Count - 14), header.trackCount);
        return new MIDI(header, null);
      }

      public static List<TrackChunk> CreateTrackChunks(List<byte> buffer, int trackCount) {
        List<TrackChunk> chunks = new List<TrackChunk>();
        int startRange = 0;
        for (int i = 0; i < trackCount; i++) {
          var chunk = new TrackChunk(buffer.GetRange(startRange, buffer.Count - startRange));
          startRange += chunk.size + 8; // size + type(4byte) + size(4byte)
          chunks.Add(chunk);
        }
        return chunks;
      }
    }
  }

  class Player {
    public static void play(MIDI.MIDI m) {
      var header = m.header;
      var tracks = m.tracks;
    }
  }
}

class Util {
  public static int Int32(List<byte> b) {
    if (BitConverter.IsLittleEndian) b.Reverse();
    return BitConverter.ToInt32(b.ToArray(), 0);
  }

  public static int Int16(List<byte> b) {
    if (BitConverter.IsLittleEndian) b.Reverse();
    return BitConverter.ToInt16(b.ToArray(), 0);
  }

  public static int Int24(List<byte> b) {
    int sum = 0;
    foreach (byte s in b) {
      sum = sum << 8;
      sum += s;
    }
    return sum;
  }
}