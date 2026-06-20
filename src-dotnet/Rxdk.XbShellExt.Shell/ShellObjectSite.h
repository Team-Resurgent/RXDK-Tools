#pragma once

class CShellObjectWithSite : public IObjectWithSite
{
public:
    CShellObjectWithSite() = default;

    virtual ~CShellObjectWithSite()
    {
        if (m_pUnknownSite)
            m_pUnknownSite->Release();
    }

    STDMETHOD(SetSite)(IUnknown* pUnkSite);
    STDMETHOD(GetSite)(REFIID riid, void** ppvSite);
    STDMETHOD(GetService)(REFGUID guidService, REFIID riid, void** ppvService);

protected:
    IUnknown* m_pUnknownSite = nullptr;
};
