#include "Precomp.h"
#include "OpenGL/GLRenderDevice.h"
#include "OpenGL/GLTexture.h"
#include <cstdio>
#include <stdexcept>
#include <thread>

// No GL handles are created; the production device, texture and deletion methods run unchanged.
std::unique_ptr<IOpenGLContext> IOpenGLContext::Create(void*, void*) { return nullptr; }

static void Require(bool condition, const char* message)
{
	if (!condition) throw std::runtime_error(message);
}

static void CheckDeletion(bool finalize)
{
	GLRenderDevice device(nullptr, nullptr, false);
	Require(!device.Context, "test context factory was not used");
	auto texture = new GLTexture();
	texture->Device = &device;
	texture->ItTexture = device.mTextures.insert(device.mTextures.end(), texture);
	auto retained = new GLTexture();
	retained->Device = &device;
	retained->ItTexture = device.mTextures.insert(device.mTextures.end(), retained);
	for (int unit = 0; unit < 10; unit++) device.SetTexture(unit, unit == 5 ? retained : texture);
	device.mNeedApply = device.mTexturesChanged = false;
	std::thread finalizer([&]() { GLRenderDevice::DeleteObject(texture); });
	finalizer.join();
	Require(device.mDeleteList.Textures.size() == 1, "worker did not enqueue the texture");
	for (int unit = 0; unit < 10; unit++)
		Require(device.mTextureUnit[unit].Tex == (unit == 5 ? retained : texture), "worker changed a render-thread binding");
	Require(!device.mNeedApply && !device.mTexturesChanged, "worker changed render-thread dirty flags");
	device.ProcessDeleteList(finalize);
	for (int unit = 0; unit < 10; unit++)
		Require(device.mTextureUnit[unit].Tex == (unit == 5 ? retained : nullptr), "render-thread drain left a stale binding");
	Require(device.mNeedApply && device.mTexturesChanged, "render-thread drain did not dirty the bindings");
	Require(device.mDeleteList.Textures.empty(), "render-thread drain did not empty the queue");
	Require(device.mTextures.size() == 1, "render-thread drain finalized the wrong texture");
	if (finalize) { Require(!texture->Device, "finalization kept its device"); delete texture; }
	GLRenderDevice::DeleteObject(retained);
	device.ProcessDeleteList();
	Require(!device.mTextureUnit[5].Tex && device.mTextures.empty(), "retained texture did not clean up");
}

int main()
{
	try
	{
		CheckDeletion(false);
		CheckDeletion(true);
		std::puts("passed 2 failed 0: worker enqueue, render-thread delete and finalize");
		return 0;
	}
	catch (const std::exception& e)
	{
		std::fprintf(stderr, "texture lifetime regression: %s\n", e.what());
		return 1;
	}
}
