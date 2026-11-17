#include "raylib.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <regex>
#include <stdexcept>
#include <string>
#include <vector>

namespace fs = std::filesystem;

struct Frame { Rectangle rect; int duration; };
struct Asset { fs::path color, normal; std::vector<Frame> frames; };

// This diagnostic reads the version 1 PixelForge export, whose rectangle
// objects contain these flat numeric fields. It is not a general JSON loader.
Asset ReadAsset(const fs::path& file)
{
    std::ifstream stream(file);
    if (!stream) throw std::runtime_error("Could not open the lighting manifest.");
    const std::string json((std::istreambuf_iterator<char>(stream)), {});
    auto field = [](const std::string& text, const std::string& key, const std::string& pattern) {
        std::smatch match;
        if (!std::regex_search(text, match, std::regex("\""+key+"\"\\s*:\\s*"+pattern)))
            throw std::runtime_error("Missing manifest field: "+key);
        return match[1].str();
    };
    auto number = [&](const std::string& text, const std::string& key) {
        return std::stoi(field(text,key,"([0-9]+)"));
    };
    const auto lighting = field(json,"lighting","(\\{[^}]*\\})");
    if (number(lighting,"version") != 1 || field(lighting,"normalSpace","\"([^\"]+)\"")!="world")
        throw std::runtime_error("Expected world-space lighting manifest v1.");
    Asset asset{
        file.parent_path()/field(lighting,"color","\"([^\"]+)\""),
        file.parent_path()/field(lighting,"normal","\"([^\"]+)\""), {}
    };
    const std::regex rect(R"rx(\{[^{}]*"durationMs"[^{}]*\{[^{}]*\}[^{}]*\})rx");
    for (auto it=std::sregex_iterator(json.begin(),json.end(),rect);it!=std::sregex_iterator();++it) {
        const auto entry=it->str();
        asset.frames.push_back({{float(number(entry,"x")),float(number(entry,"y")),
            float(number(entry,"width")),float(number(entry,"height"))},number(entry,"durationMs")});
    }
    if (asset.frames.empty() || int(asset.frames.size())!=number(json,"frameCount"))
        throw std::runtime_error("Invalid frame list.");
    return asset;
}

constexpr const char* fragment=R"glsl(
#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform sampler2D texture0;
uniform sampler2D normals;
uniform vec3 lightDir;
out vec4 finalColor;
void main() {
    vec4 color = texture(texture0, fragTexCoord);
    vec3 N = normalize(texture(normals, fragTexCoord).rgb*2.0-1.0);
    float shade = 0.25 + 0.75*max(dot(N,normalize(lightDir)),0.0);
    finalColor = vec4(color.rgb*shade,color.a)*fragColor;
}
)glsl";

constexpr std::array<Vector3,5> lights{{{1,0,0},{-1,0,0},{0,1,0},{0,-1,0},{0,0,1}}};
constexpr std::array<const char*,5> names{{"East","West","North","South","Above"}};

void DrawLit(Texture2D color, Texture2D normal, Shader shader, int mapLoc, int lightLoc,
             Vector3 light, Rectangle frame, Rectangle dest)
{
    BeginShaderMode(shader);
    SetShaderValueTexture(shader,mapLoc,normal);
    SetShaderValue(shader,lightLoc,&light,SHADER_UNIFORM_VEC3);
    DrawTexturePro(color,frame,dest,{},0,WHITE);
    // The active texture binding is released at batch flush.
    EndShaderMode();
}

void Verify(const Asset& asset, Texture2D color, Texture2D normals, Shader shader,
            int mapLoc, int lightLoc, const fs::path& output)
{
    fs::create_directories(output);
    Image source=LoadImage(asset.color.string().c_str());
    Image vectors=LoadImage(asset.normal.string().c_str());
    int width=int(asset.frames[0].rect.width),height=int(asset.frames[0].rect.height);
    RenderTexture2D target=LoadRenderTexture(width,height);
    for (size_t frame=0;frame<asset.frames.size();++frame) {
        for (size_t axis=0;axis<lights.size();++axis) {
            const auto rect=asset.frames[frame].rect;
            BeginTextureMode(target);
            ClearBackground(BLANK);
            DrawLit(color,normals,shader,mapLoc,lightLoc,lights[axis],rect,{0,0,float(width),float(height)});
            EndTextureMode();
            Image result=LoadImageFromTexture(target.texture);
            ImageFlipVertical(&result);
            for(int y=0;y<height;++y) for(int x=0;x<width;++x) {
                const auto c=GetImageColor(source,int(rect.x)+x,int(rect.y)+y);
                const auto n=GetImageColor(vectors,int(rect.x)+x,int(rect.y)+y);
                const auto gpu=GetImageColor(result,x,y);
                if(c.a!=n.a || c.a!=gpu.a) throw std::runtime_error("Alpha, frame rectangle, or UV orientation mismatch.");
                if(!c.a) continue;
                const float nx=n.r/127.5f-1,ny=n.g/127.5f-1,nz=n.b/127.5f-1;
                const float length=std::sqrt(nx*nx+ny*ny+nz*nz);
                const auto l=lights[axis];
                const float shade=.25f+.75f*std::max(0.f,(nx*l.x+ny*l.y+nz*l.z)/length);
                if(std::abs(gpu.r-c.r*shade)>2 || std::abs(gpu.g-c.g*shade)>2 || std::abs(gpu.b-c.b*shade)>2)
                    throw std::runtime_error("GPU diffuse result differs from linear byte decode.");
            }
            if(frame==0 && !ExportImage(result,(output/(std::string(names[axis])+".png")).string().c_str()))
                throw std::runtime_error("Could not save lighting evidence.");
            UnloadImage(result);
        }
    }
    UnloadRenderTexture(target);
    UnloadImage(source);
    UnloadImage(vectors);
    std::cout<<"Verified GPU colors and alpha for "<<asset.frames.size()<<" frames x five directions.\n";
}

int main(int argc,char** argv)
{
    if (argc!=2 && argc!=3) {
        std::cerr<<"Usage: lighting-validator <asset.lighting.json> [verification-image-folder]\n";
        return 2;
    }
    try {
        const auto asset=ReadAsset(fs::u8path(argv[1]));
        if(argc==3) SetConfigFlags(FLAG_WINDOW_HIDDEN);
        InitWindow(830,430,"PixelForge lighting validator");
        const auto color=LoadTexture(asset.color.string().c_str());
        const auto normals=LoadTexture(asset.normal.string().c_str());
        if(!IsTextureValid(color)||!IsTextureValid(normals)||color.width!=normals.width||color.height!=normals.height)
            throw std::runtime_error("Invalid or mismatched atlases.");
        SetTextureFilter(color,TEXTURE_FILTER_POINT);
        SetTextureFilter(normals,TEXTURE_FILTER_POINT);
        const auto shader=LoadShaderFromMemory(nullptr,fragment);
        const int mapLoc=GetShaderLocation(shader,"normals"),lightLoc=GetShaderLocation(shader,"lightDir");
        if(mapLoc<0||lightLoc<0)throw std::runtime_error("Could not compile the diffuse shader.");
        if(argc==3) Verify(asset,color,normals,shader,mapLoc,lightLoc,fs::u8path(argv[2]));
        else {
            SetTargetFPS(60);
            size_t axis=0,frame=0;
            double elapsed=0;
            while(!WindowShouldClose()) {
                if(IsKeyPressed(KEY_RIGHT)) axis=(axis+1)%lights.size();
                if(IsKeyPressed(KEY_LEFT)) axis=(axis+lights.size()-1)%lights.size();
                elapsed+=1000*GetFrameTime();
                if(elapsed>=asset.frames[frame].duration) {
                    elapsed-=asset.frames[frame].duration;
                    frame=(frame+1)%asset.frames.size();
                }
                const auto rect=asset.frames[frame].rect;
                const float zoom=std::max(1.f,std::floor(std::min(256/rect.width,320/rect.height)));
                const Rectangle dest{0,70,rect.width*zoom,rect.height*zoom};
                BeginDrawing();
                ClearBackground({37,40,47,255});
                DrawText("Left / Right changes the world-space light direction",20,20,20,RAYWHITE);
                DrawText("Base color",20,50,18,RAYWHITE);
                DrawText("Normal map",290,50,18,RAYWHITE);
                DrawText(TextFormat("Lit from %s",names[axis]),560,50,18,RAYWHITE);
                auto panel=dest;
                panel.x=20;DrawTexturePro(color,rect,panel,{},0,WHITE);
                panel.x=290;DrawTexturePro(normals,rect,panel,{},0,WHITE);
                panel.x=560;DrawLit(color,normals,shader,mapLoc,lightLoc,lights[axis],rect,panel);
                EndDrawing();
            }
        }
        UnloadShader(shader);
        UnloadTexture(normals);
        UnloadTexture(color);
        CloseWindow();
        return 0;
    } catch(const std::exception& error) {
        std::cerr<<error.what()<<'\n';
        if(IsWindowReady()) CloseWindow();
        return 1;
    }
}
